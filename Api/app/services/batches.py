from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.errors import BatchCreationError
from app.models import Batch, Farm, Warehouse
from app.schemas.batch import BatchCreate

STATUS_ON_CREATION = "compliant"


async def create_batch(session: AsyncSession, payload: BatchCreate) -> Batch:
    """Create a batch from an ERP reception file.

    Resolves the ERP references to our own ids, and refuses the batch when
    either reference is unknown — the file then goes to `error/` rather than
    creating a batch attached to the wrong farm or warehouse.

    `status` is imposed here, never read from the file: the ERP has no opinion
    on storage compliance.
    """
    farm_id = await session.scalar(
        select(Farm.id).where(Farm.external_ref == payload.farm_ref)
    )
    if farm_id is None:
        raise BatchCreationError(
            "farm_not_found", f"No farm found with external_ref {payload.farm_ref!r}."
        )

    warehouse_id = await session.scalar(
        select(Warehouse.id).where(Warehouse.external_ref == payload.warehouse_ref)
    )
    if warehouse_id is None:
        raise BatchCreationError(
            "warehouse_not_found",
            f"No warehouse found with external_ref {payload.warehouse_ref!r}.",
        )

    batch = Batch(
        batch_ref=payload.batch_ref,
        farm_id=farm_id,
        warehouse_id=warehouse_id,
        quality_grade=payload.quality_grade,
        stored_at=payload.stored_at,
        status=STATUS_ON_CREATION,
    )
    session.add(batch)

    # No pre-read on batch_ref: a SELECT-then-INSERT leaves a window in which a
    # replayed file creates the duplicate it was meant to prevent. The UNIQUE
    # constraint is what actually holds the rule, so it is what we catch. The
    # farm and warehouse FKs were just resolved above, which leaves batch_ref as
    # the only constraint this insert can realistically violate.
    try:
        await session.commit()
    except IntegrityError as exc:
        await session.rollback()
        raise BatchCreationError(
            "batch_ref_already_exists",
            f"A batch already exists with batch_ref {payload.batch_ref!r}.",
        ) from exc

    await session.refresh(batch)
    return batch
