import uuid
from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Query, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.errors import WarehouseRefUnknownError
from app.schemas.alert import AlertList, AlertStatusFilter, AlertSummary
from app.security import require_api_key
from app.services.alerts import AlertNotFoundError, list_alerts, resolve_alert

router = APIRouter(prefix="/api", tags=["alerts"])


@router.get("/alerts", response_model=AlertList)
async def read_alerts(
    session: Annotated[AsyncSession, Depends(get_session)],
    warehouse_ref: Annotated[str, Query(min_length=1, max_length=64)],
    status_filter: Annotated[AlertStatusFilter, Query(alias="status")] = (
        AlertStatusFilter.ACTIVE
    ),
    limit: Annotated[int, Query(ge=1, le=200)] = 50,
) -> AlertList:
    """A warehouse's alerts, newest first.

    `warehouse_ref` is required and comes from head office, which holds the
    session: it verifies the JWT, resolves the user's warehouse and passes the
    reference on. This API has no login of its own — the same split as
    `GET /api/measurements` and `GET /api/notifications`.

    An unknown reference answers `404`, not an empty list. "This warehouse has
    nothing open" and "you asked for a warehouse that does not exist" mean very
    different things to the caller, and one body for both is how a quiet screen
    hides a typo in a configuration file.
    """
    try:
        return await list_alerts(
            session, warehouse_ref=warehouse_ref, status=status_filter, limit=limit
        )
    except WarehouseRefUnknownError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"code": exc.code, "message": exc.message},
        ) from exc


@router.patch(
    "/alerts/{alert_id}/resolve",
    response_model=AlertSummary,
    dependencies=[Depends(require_api_key)],
)
async def patch_alert_resolve(
    alert_id: uuid.UUID,
    session: Annotated[AsyncSession, Depends(get_session)],
    warehouse_ref: Annotated[str, Query(min_length=1, max_length=64)],
) -> AlertSummary:
    """Close an alert once the room is back under control.

    Authenticated, unlike the listing above: this one writes. Same rule as
    `POST /api/batches` and `PATCH /api/notifications/{id}/read` — a route that
    changes the database never falls back to open.

    More than housekeeping: `alerts` allows a single active `condition` alert
    per warehouse, so a room whose alert is never resolved can never raise
    another one. This is what re-arms the detection.

    Idempotent: resolving an already-resolved alert returns it unchanged rather
    than moving its timestamp.

    An alert belonging to another warehouse answers `404`, not `403`. The
    warehouse is part of the lookup rather than a check made after finding the
    row, so the route never confirms an id exists to a caller with no business
    knowing it.
    """
    try:
        return await resolve_alert(
            session, alert_id=alert_id, warehouse_ref=warehouse_ref
        )
    except (WarehouseRefUnknownError, AlertNotFoundError) as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"code": exc.code, "message": exc.message},
        ) from exc
