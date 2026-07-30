from datetime import datetime
from enum import Enum

from pydantic import BaseModel, ConfigDict


class QualityGrade(str, Enum):
    """Values accepted for a batch quality.

    Must mirror the `quality_grade` enum type in the database exactly. The
    grades below follow the SCA green-coffee scale and are NOT confirmed by the
    spec — this is the single place to change once the team fixes the list.
    """

    GRADE_1_SPECIALTY = "grade_1_specialty"
    GRADE_2_PREMIUM = "grade_2_premium"
    GRADE_3_EXCHANGE = "grade_3_exchange"
    GRADE_4_STANDARD = "grade_4_standard"


class BatchCreate(BaseModel):
    """Batch as submitted by a logged-in warehouse user.

    Only these two fields are read. Everything else the batch needs is imposed
    by the server, so any other key in the body — including warehouse_id or
    user_id — is dropped instead of overriding the server's own values.
    """

    model_config = ConfigDict(extra="ignore")

    farm_id: int
    quality: QualityGrade


class BatchRead(BaseModel):
    """Batch as persisted. Complete, so the frontend can redirect and display
    it without a second request.
    """

    model_config = ConfigDict(from_attributes=True)

    id: int
    farm_id: int
    warehouse_id: int
    user_id: int
    quality: QualityGrade
    entered_at: datetime
    status: str
    is_compliant: bool
