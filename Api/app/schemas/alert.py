"""An alert, as a warehouse reads it and acknowledges it.

Named `AlertSummary` and not `AlertRead`: `schemas/batch_history.py` already
holds an `AlertRead`, shaped for shading a curve — it carries `concernsBatch`
and no reference. The two answer different questions and must not be merged
into one model that serves neither well.

The sentence is composed at read time, like a notification's: an alert points
at a warehouse and sometimes a batch, and a message frozen at write time would
still name a batch by a reference that has since changed.

camelCase on the wire, like every other route of this API — the reader is head
office's .NET relay.
"""

import uuid
from datetime import datetime
from enum import Enum

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class AlertStatusFilter(str, Enum):
    """What `GET /api/alerts` is asked to return.

    `ACTIVE` is the default because the bell shows what is still open. `ALL`
    exists for the quality page, which shades resolved periods too and would
    otherwise need a second route.
    """

    ACTIVE = "active"
    RESOLVED = "resolved"
    ALL = "all"


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class AlertSummary(CamelModel):
    """One alert, ready to display and to route on click.

    `batchId` and `batchRef` are null on a `condition` alert: it concerns the
    room, not one batch — the model reserves `batch_id` to `expiration`. The
    frontend needs to tell the two apart to know whether a click opens a batch,
    and `alertType` alone already says it.
    """

    alert_id: uuid.UUID
    alert_type: str
    alert_status: str
    message: str
    batch_id: uuid.UUID | None
    batch_ref: str | None
    created_at: datetime
    resolved_at: datetime | None


class AlertList(CamelModel):
    """Wrapped rather than a bare array, like `NotificationList`.

    `activeCount` is always the full number of open alerts, never the count of
    what this page returned: a caller asking for the last 20 still has to
    display the right total.
    """

    alerts: list[AlertSummary]
    active_count: int
