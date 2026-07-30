from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.errors import BatchCreationError
from app.models import Batch, Farm
from app.schemas.batch import BatchCreate
from app.security import CurrentUser

STATUS_ON_CREATION = "received"


async def create_batch(
    session: AsyncSession,
    payload: BatchCreate,
    current_user: CurrentUser,
    now: datetime | None = None,
) -> Batch:
    """Create a batch for the caller's warehouse.

    Every field other than farm_id and quality is imposed here, never taken
    from the request body. Raises BatchCreationError if farm_id is unknown, in
    which case no batch is persisted.
    """
    farm_exists = await session.scalar(
        select(Farm.id).where(Farm.id == payload.farm_id)
    )
    if farm_exists is None:
        raise BatchCreationError(
            "farm_not_found", f"No farm found with id {payload.farm_id}."
        )

    batch = Batch(
        farm_id=payload.farm_id,
        quality=payload.quality.value,
        warehouse_id=current_user.warehouse_id,
        user_id=current_user.id,
        entered_at=now or datetime.now(UTC),
        status=STATUS_ON_CREATION,
        is_compliant=True,
    )
    session.add(batch)
    await session.commit()
    await session.refresh(batch)
    return batch
