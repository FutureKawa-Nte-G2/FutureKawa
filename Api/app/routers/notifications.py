import uuid
from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Query, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemas.notification import NotificationList, NotificationRead
from app.security import require_api_key
from app.services.notifications import (
    NotificationNotFoundError,
    WarehouseRefUnknownError,
    list_notifications,
    mark_read,
)

router = APIRouter(prefix="/api", tags=["notifications"])


@router.get("/notifications", response_model=NotificationList)
async def read_notifications(
    session: Annotated[AsyncSession, Depends(get_session)],
    warehouse_ref: Annotated[str, Query(min_length=1, max_length=64)],
    unread: Annotated[bool, Query()] = False,
    limit: Annotated[int, Query(ge=1, le=200)] = 50,
) -> NotificationList:
    """A warehouse's notifications, newest first.

    `warehouse_ref` is required and comes from head office, which holds the
    session: it verifies the JWT, resolves the user's warehouse and passes the
    reference on. This API has no login of its own — the same split as
    `GET /api/measurements`, which is also served unauthenticated to their relay.

    An unknown reference answers `404`, not an empty list. The two mean very
    different things to the caller — "this warehouse has nothing to show" and
    "you asked for a warehouse that does not exist" — and returning the same
    body for both is how an empty bell hides a typo in a configuration file.
    """
    try:
        return await list_notifications(
            session, warehouse_ref=warehouse_ref, unread_only=unread, limit=limit
        )
    except WarehouseRefUnknownError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"code": exc.code, "message": exc.message},
        ) from exc


@router.patch(
    "/notifications/{notification_id}/read",
    response_model=NotificationRead,
    dependencies=[Depends(require_api_key)],
)
async def patch_notification_read(
    notification_id: uuid.UUID,
    session: Annotated[AsyncSession, Depends(get_session)],
    warehouse_ref: Annotated[str, Query(min_length=1, max_length=64)],
) -> NotificationRead:
    """Mark a notification as seen.

    Authenticated, unlike the listing above: this one writes. Same rule as
    `POST /api/batches` — a route that changes the database never falls back to
    open.

    Idempotent: clicking an already-read notification returns it unchanged
    rather than moving its timestamp.

    A notification belonging to another warehouse answers `404`, not `403`. The
    warehouse is part of the lookup rather than a check made after finding the
    row, so the route never confirms an id exists to a caller with no business
    knowing it.
    """
    try:
        return await mark_read(
            session, notification_id=notification_id, warehouse_ref=warehouse_ref
        )
    except (WarehouseRefUnknownError, NotificationNotFoundError) as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"code": exc.code, "message": exc.message},
        ) from exc
