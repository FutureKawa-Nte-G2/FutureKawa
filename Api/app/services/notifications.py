from collections.abc import Sequence
from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Notification
from app.security import CurrentUser


class NotificationNotFoundError(Exception):
    """No notification with that id in the caller's warehouse."""

    code = "notification_not_found"

    def __init__(self, notification_id: int) -> None:
        self.message = f"No notification found with id {notification_id}."
        super().__init__(self.message)


async def list_notifications(
    session: AsyncSession,
    current_user: CurrentUser,
    unread: bool = False,
) -> Sequence[Notification]:
    """List the notifications of the caller's warehouse, most recent first."""
    query = select(Notification).where(
        Notification.warehouse_id == current_user.warehouse_id
    )
    if unread:
        query = query.where(Notification.read_at.is_(None))

    # id as tie-breaker: created_at alone leaves rows sharing an instant in an
    # undefined order, which makes both the UI and the tests non-deterministic.
    query = query.order_by(Notification.created_at.desc(), Notification.id.desc())

    return (await session.scalars(query)).all()


async def mark_notification_read(
    session: AsyncSession,
    notification_id: int,
    current_user: CurrentUser,
    now: datetime | None = None,
) -> Notification:
    """Stamp read_at on a notification of the caller's warehouse.

    Idempotent: an already-read notification keeps its original read_at.

    Scoping the lookup to the caller's warehouse makes a notification belonging
    to another warehouse indistinguishable from one that does not exist, so the
    endpoint never confirms an id the caller has no right to see.
    """
    notification = await session.scalar(
        select(Notification).where(
            Notification.id == notification_id,
            Notification.warehouse_id == current_user.warehouse_id,
        )
    )
    if notification is None:
        raise NotificationNotFoundError(notification_id)

    if notification.read_at is None:
        notification.read_at = now or datetime.now(UTC)
        await session.commit()
        await session.refresh(notification)

    return notification
