import uuid
from datetime import date

from pydantic import BaseModel, ConfigDict, Field, field_validator
from pydantic.alias_generators import to_camel

from app.models import QUALITY_GRADES


class BatchCreate(BaseModel):
    """A batch as described by an ERP reception file.

    Everything comes from the file. A batch is no longer typed in by a human, so
    nothing is derived from a caller identity: the warehouse is named by the
    file, not carried by a token.

    Farm and warehouse are given by their ERP reference, never by our internal
    id: the ERP does not know our ids, and no callback ever tells it.
    `farms.farm_ref` and `warehouses.warehouse_ref` are the only keys the two
    systems share, which is exactly what the MLD reserves them for.

    camelCase on the wire, like `/api/measurements` and like the frontend's own
    `Batch` type. `populate_by_name` keeps snake_case accepted too, so a caller
    written against the column names is not punished for it.

    Extra keys are dropped rather than allowed to reach a server-owned field: a
    file carrying `batchStatus` or `batchId` is honoured for everything else and
    silently loses those two.
    """

    model_config = ConfigDict(
        alias_generator=to_camel, populate_by_name=True, extra="ignore"
    )

    # Lengths mirror varchar(64) in the schema. Enforced here so an oversized
    # reference is a 422 naming the field, not a database error surfacing as a
    # 500 the watcher cannot act on.
    batch_ref: str = Field(min_length=1, max_length=64)
    farm_ref: str = Field(min_length=1, max_length=64)
    warehouse_ref: str = Field(min_length=1, max_length=64)
    stored_at: date
    quality_grade: str | None = None

    @field_validator("quality_grade")
    @classmethod
    def _normalise_grade(cls, value: str | None) -> str | None:
        """Accept the ERP's casing, store ours.

        Odoo's `quality_grade` is a Selection of `a`/`b`/`c` while our shared
        vocabulary is `A`/`B`/`C`. Converting case at the boundary is the rule
        already recorded in `Api/CLAUDE.md` for `batch_status`; the same applies
        here rather than teaching the database two spellings of one grade.

        An empty string is treated as no grade: it is what a file exports when
        Odoo computed nothing, and it would otherwise be stored as a grade of
        its own.
        """
        if value is None or not value.strip():
            return None

        normalised = value.strip().upper()
        if normalised not in QUALITY_GRADES:
            raise ValueError(
                f"quality_grade must be one of {', '.join(QUALITY_GRADES)}."
            )
        return normalised


class BatchRead(BaseModel):
    """A batch as persisted, complete.

    Every field is returned, including the ones the server owns, so the caller
    can log or display what it created without a second request.
    """

    model_config = ConfigDict(
        from_attributes=True, alias_generator=to_camel, populate_by_name=True
    )

    batch_id: uuid.UUID
    batch_ref: str
    farm_id: uuid.UUID
    warehouse_id: uuid.UUID
    stored_at: date
    shipped_at: date | None
    quality_grade: str | None
    batch_status: str
