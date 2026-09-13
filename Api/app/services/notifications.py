"""Reading and acknowledging a warehouse's notifications.

The warehouse is named by `warehouse_ref`, never by an internal id, and never
inferred from the caller. Head office holds the session: it verifies the JWT,
resolves which warehouse the user belongs to, and passes the reference on. This
API has no login of its own, and a notification is not sensitive enough to
justify building one — the same split as `GET /api/measurements`.
"""

import uuid
from datetime import UTC, datetime

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

# Re-exported rather than redefined: `/api/alerts` raises the same one, and the
# router imports it from here.
from app.errors import WarehouseRefUnknownError
from app.models import Batch, Notification, Order, Warehouse
from app.schemas.notification import NotificationList, NotificationRead

__all__ = [
    "NotificationNotFoundError",
    "WarehouseRefUnknownError",
    "list_notifications",
    "mark_read",
]


class NotificationNotFoundError(Exception):
    code = "notification_not_found"

    def __init__(self, notification_id: uuid.UUID) -> None:
        self.message = f"No notification found with id {notification_id}."
        super().__init__(self.message)


def _message(notification: Notification, batch_ref: str | None, order_ref: str | None) -> str:
    """The sentence the bell shows.

    Kept short and specific: the bell lists several at once, and the reader has
    to tell them apart at a glance rather than read a paragraph.
    """
    if notification.notification_type == "batch_non_compliant":
        return f"Lot {batch_ref} non conforme"
    if notification.notification_type == "order_received":
        return f"Commande {order_ref} à préparer"
    # A type written by a producer this API does not know about yet. Showing
    # the raw type beats hiding the row: the warehouse still sees that
    # something happened, and the gap is visible rather than silent.
    return notification.notification_type


async def _resolve_warehouse(session: AsyncSession, warehouse_ref: str) -> Warehouse:
    warehouse = await session.scalar(
        select(Warehouse).where(Warehouse.warehouse_ref == warehouse_ref)
    )
    if warehouse is None:
        raise WarehouseRefUnknownError(warehouse_ref)
    return warehouse


async def list_notifications(
    session: AsyncSession,
    warehouse_ref: str,
    unread_only: bool = False,
    limit: int = 50,
) -> NotificationList:
    """A warehouse's notifications, newest first.

    `unreadCount` is always the full unread total, never the count of what this
    page returned: the badge shows how many are waiting, and a caller asking for
    the last 20 still has to display the right number.
    """
    warehouse = await _resolve_warehouse(session, warehouse_ref)

    query = (
        select(Notification, Batch.batch_ref, Order.order_reference)
        .outerjoin(Batch, Batch.batch_id == Notification.batch_id)
        .outerjoin(Order, Order.id == Notification.order_id)
        .where(Notification.warehouse_id == warehouse.warehouse_id)
        .order_by(Notification.created_at.desc())
        .limit(limit)
    )
    if unread_only:
        query = query.where(Notification.read_at.is_(None))

    rows = (await session.execute(query)).all()

    unread_count = await session.scalar(
        select(func.count())
        .select_from(Notification)
        .where(
            Notification.warehouse_id == warehouse.warehouse_id,
            Notification.read_at.is_(None),
        )
    )

    return NotificationList(
        notifications=[
            NotificationRead(
                notification_id=notification.notification_id,
                notification_type=notification.notification_type,
                message=_message(notification, batch_ref, order_ref),
                batch_id=notification.batch_id,
                order_id=notification.order_id,
                created_at=notification.created_at,
                read_at=notification.read_at,
            )
            for notification, batch_ref, order_ref in rows
        ],
        unread_count=unread_count or 0,
    )


async def mark_read(
    session: AsyncSession,
    notification_id: uuid.UUID,
    warehouse_ref: str,
    now: datetime | None = None,
) -> NotificationRead:
    """Stamp a notification as seen.

    The warehouse is part of the lookup, not a check made afterwards: a
    notification belonging to another warehouse answers `404` rather than
    `403`, so the route never confirms that an id exists to someone who has no
    business knowing.

    Idempotent. Marking an already-read notification returns it unchanged
    rather than moving its timestamp — the bell fires this on every click, and
    the question it answers is "when was this first seen".
    """
    warehouse = await _resolve_warehouse(session, warehouse_ref)

    row = (
        await session.execute(
            select(Notification, Batch.batch_ref, Order.order_reference)
            .outerjoin(Batch, Batch.batch_id == Notification.batch_id)
            .outerjoin(Order, Order.id == Notification.order_id)
            .where(
                Notification.notification_id == notification_id,
                Notification.warehouse_id == warehouse.warehouse_id,
            )
        )
    ).first()

    if row is None:
        raise NotificationNotFoundError(notification_id)

    notification, batch_ref, order_ref = row

    if notification.read_at is None:
        notification.read_at = now or datetime.now(UTC)
        await session.commit()
        await session.refresh(notification)

    return NotificationRead(
        notification_id=notification.notification_id,
        notification_type=notification.notification_type,
        message=_message(notification, batch_ref, order_ref),
        batch_id=notification.batch_id,
        order_id=notification.order_id,
        created_at=notification.created_at,
        read_at=notification.read_at,
    )
