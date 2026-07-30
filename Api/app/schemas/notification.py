from datetime import datetime

from pydantic import BaseModel, ConfigDict


class NotificationRead(BaseModel):
    """Notification as returned to the frontend."""

    model_config = ConfigDict(from_attributes=True)

    id: int
    warehouse_id: int
    message: str
    created_at: datetime
    read_at: datetime | None
