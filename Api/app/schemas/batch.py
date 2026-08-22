from datetime import date

from pydantic import BaseModel, ConfigDict, Field


class BatchCreate(BaseModel):
    """Batch as described by an ERP reception file.

    Everything comes from the file. A batch is no longer typed in by a human,
    so nothing is derived from a caller identity: the warehouse is named by the
    file, not carried by a token.

    Farm and warehouse are given by their ERP reference, never by our internal
    id: the ERP does not know our ids, and no callback ever tells it. Extra keys
    are dropped rather than allowed to set a server-owned field.
    """

    model_config = ConfigDict(extra="ignore")

    batch_ref: str = Field(min_length=1)
    farm_ref: str = Field(min_length=1)
    warehouse_ref: str = Field(min_length=1)
    stored_at: date
    quality_grade: str | None = None


class BatchRead(BaseModel):
    """Batch as persisted, complete, so the watcher can log what it created
    without a second request.
    """

    model_config = ConfigDict(from_attributes=True)

    id: int
    batch_ref: str
    farm_id: int
    warehouse_id: int
    quality_grade: str | None
    stored_at: date
    shipped_at: date | None
    status: str
