"""Creation of a batch from an ERP reception file.

The caller is a file watcher, not a person. Two consequences shape everything
here: no field can be derived from a caller identity, and every rejection has to
name its reason in a way a program can branch on — a watcher routing a file to
`error/` reads `detail.code`, it does not read prose.

Writing is local by design. Nothing in this module talks to head office: a
country keeps receiving batches while the link is down, which is the whole point
of an autonomous backend per country.
"""

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Batch, Farm, Warehouse
from app.schemas.batch import BatchCreate

# A batch arriving from a reception file enters storage. `Api/CLAUDE.md` records
# the rule, and head office writes the same thing on its side
# (`BatchStatus.Stored` in `OrderService`).
NEW_BATCH_STATUS = "stored"


class BatchAlreadyExistsError(Exception):
    """A batch with that reference is already stored."""

    code = "batch_already_exists"

    def __init__(self, batch_ref: str) -> None:
        self.message = f"A batch with reference {batch_ref} already exists."
        super().__init__(self.message)


class FarmRefUnknownError(Exception):
    """No farm carries that ERP reference."""

    code = "farm_ref_unknown"

    def __init__(self, farm_ref: str) -> None:
        self.message = f"No farm found with reference {farm_ref}."
        super().__init__(self.message)


class WarehouseRefUnknownError(Exception):
    """No warehouse carries that ERP reference."""

    code = "warehouse_ref_unknown"

    def __init__(self, warehouse_ref: str) -> None:
        self.message = f"No warehouse found with reference {warehouse_ref}."
        super().__init__(self.message)


async def create_batch(session: AsyncSession, payload: BatchCreate) -> Batch:
    """Persist one batch, resolving the ERP references to our own ids.

    Both references are resolved before anything is written, so a file naming an
    unknown farm leaves the database exactly as it was. Resolving them one at a
    time also means the error names which of the two is wrong, rather than
    reporting a generic bad reference the watcher cannot act on.
    """
    farm_id = await session.scalar(
        select(Farm.farm_id).where(Farm.farm_ref == payload.farm_ref)
    )
    if farm_id is None:
        raise FarmRefUnknownError(payload.farm_ref)

    warehouse_id = await session.scalar(
        select(Warehouse.warehouse_id).where(
            Warehouse.warehouse_ref == payload.warehouse_ref
        )
    )
    if warehouse_id is None:
        raise WarehouseRefUnknownError(payload.warehouse_ref)

    batch = Batch(
        warehouse_id=warehouse_id,
        farm_id=farm_id,
        batch_ref=payload.batch_ref,
        stored_at=payload.stored_at,
        # A batch enters the FIFO the moment it is created: `shipped_at` is what
        # marks it as gone, and the FIFO listing reads exactly this column.
        shipped_at=None,
        quality_grade=payload.quality_grade,
        batch_status=NEW_BATCH_STATUS,
    )
    session.add(batch)

    try:
        await session.commit()
    except IntegrityError as exc:
        # The unique index on `batch_ref` is what makes a replayed reception
        # file harmless, and it is the only unique constraint on the table
        # besides a primary key we generate ourselves — so an integrity error
        # here is a duplicate reference and nothing else.
        #
        # Rolling back matters beyond this request: without it the session
        # stays in a failed transaction and every later statement on it raises,
        # which would let one bad file poison the files behind it.
        await session.rollback()
        raise BatchAlreadyExistsError(payload.batch_ref) from exc

    return batch
