"""What the bell displays, as head office relays it.

The wording is built here rather than stored: a notification carries a type and
a foreign key, and the sentence is composed at read time. A message frozen at
write time would still name a batch by a reference that has since changed, and
translating it later would mean rewriting rows.

camelCase on the wire, like every other route of this API — the reader is head
office's .NET relay.
"""

import uuid
from datetime import datetime

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class NotificationRead(CamelModel):
    """One notification, ready to display and to route on click.

    `batchId` and `orderId` are both returned even though only one is ever
    filled: the frontend routes on them — a `batch_non_compliant` opens the
    batch, an `order_received` opens the order (US #30) — and a single
    nullable field would force it to branch on the type to know what the id
    means.
    """

    notification_id: uuid.UUID
    notification_type: str
    message: str
    batch_id: uuid.UUID | None
    order_id: uuid.UUID | None
    created_at: datetime
    read_at: datetime | None


class NotificationList(CamelModel):
    """Wrapped rather than a bare array.

    A bare array leaves no room to add a total or a cursor later without
    breaking the reader, and the frontend already types its list responses this
    way (`AlertListResponse`, `BatchListResponse`).
    """

    notifications: list[NotificationRead]
    unread_count: int
