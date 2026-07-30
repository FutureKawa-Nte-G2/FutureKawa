from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemas.notification import NotificationRead
from app.security import CurrentUser, get_current_user
from app.services.notifications import (
    NotificationNotFoundError,
    list_notifications,
    mark_notification_read,
)

router = APIRouter(prefix="/api/notifications", tags=["notifications"])


@router.get("", response_model=list[NotificationRead])
async def get_notifications(
    unread: bool = False,
    session: AsyncSession = Depends(get_session),
    current_user: CurrentUser = Depends(get_current_user),
) -> list[NotificationRead]:
    """List the notifications of the caller's warehouse, newest first.

    A `warehouse_id` query parameter appears in the documented URL but is never
    read: the warehouse always comes from the token, so a local user cannot list
    another warehouse's notifications by editing the query string.
    """
    notifications = await list_notifications(session, current_user, unread=unread)
    return [NotificationRead.model_validate(item) for item in notifications]


@router.patch("/{notification_id}/read", response_model=NotificationRead)
async def patch_notification_read(
    notification_id: int,
    session: AsyncSession = Depends(get_session),
    current_user: CurrentUser = Depends(get_current_user),
) -> NotificationRead:
    """Mark a notification of the caller's warehouse as read."""
    try:
        notification = await mark_notification_read(
            session, notification_id, current_user
        )
    except NotificationNotFoundError as exc:
        raise HTTPException(
            status_code=404,
            detail={"code": exc.code, "message": exc.message},
        ) from exc

    return NotificationRead.model_validate(notification)
