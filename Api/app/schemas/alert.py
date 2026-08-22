from datetime import datetime
from decimal import Decimal
from enum import Enum

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class AlertType(str, Enum):
    """Must mirror the `alert_type` enum in the database."""

    CONDITION = "condition"
    EXPIRATION = "expiration"


class AlertState(str, Enum):
    """Must mirror the `alert_state` enum in the database."""

    ACTIVE = "active"
    RESOLVED = "resolved"


class AlertRead(BaseModel):
    """One alert, as pulled by head office.

    Serialised in camelCase because the consumer is the .NET head office, which
    deserialises with `PropertyNameCaseInsensitive`. That option tolerates a
    different case, NOT a different name: `warehouse_ref` would not bind to
    `WarehouseRef`, it would land as null without raising. Same convention as
    the measurements contract.

    An alert names its warehouse by ERP reference, never by our internal id:
    head office has its own ids and no way to map ours.
    """

    model_config = ConfigDict(
        alias_generator=to_camel, populate_by_name=True, from_attributes=True
    )

    id: int
    type: AlertType
    state: AlertState
    # Always filled: a `condition` alert carries its warehouse directly, an
    # `expiration` alert gets it through the batch it concerns.
    warehouse_ref: str
    # Only an `expiration` alert names a batch.
    batch_ref: str | None
    # Reading that tripped the threshold, or age in days for an expiration.
    value: Decimal | None
    created_at: datetime
    resolved_at: datetime | None
