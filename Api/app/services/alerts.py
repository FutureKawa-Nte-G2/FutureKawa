"""Reading and resolving a warehouse's alerts.

`quality.py` opens an alert; nothing until now closed one. That gap was not a
missing convenience, it was a dead end: `alerts` carries a partial unique index
allowing a single active `condition` alert per warehouse, so an alert that is
never resolved makes every later one impossible — the second breach in a room
hits the IntegrityError branch of `evaluate_reading` and is quietly downgraded
to a batch flag. The room stops being able to raise its hand.

Resolving is therefore what re-arms the detection, not just what tidies a list.

The warehouse is named by `warehouse_ref`, never by an internal id, and never
inferred from the caller — the same split as `/api/notifications`: head office
holds the session, resolves which warehouse the user belongs to, and passes the
reference on.
"""

import uuid
from datetime import UTC, datetime

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.errors import WarehouseRefUnknownError
from app.models import Alert, Batch, Warehouse
from app.schemas.alert import AlertList, AlertStatusFilter, AlertSummary

ACTIVE = "active"
RESOLVED = "resolved"


class AlertNotFoundError(Exception):
    code = "alert_not_found"

    def __init__(self, alert_id: uuid.UUID) -> None:
        self.message = f"No alert found with id {alert_id}."
        super().__init__(self.message)


def _message(alert: Alert, batch_ref: str | None) -> str:
    """The sentence the list shows.

    Short and specific: the caller lists several at once and has to tell them
    apart at a glance. The warehouse is not named — it asked by reference, and
    repeating it on every row would say nothing.
    """
    if alert.alert_type == "condition":
        return "Conditions hors plage dans l'entrepôt"
    if alert.alert_type == "expiration":
        return f"Lot {batch_ref} périmé"
    # A type written by a producer this service does not know about yet.
    # Showing the raw type beats hiding the row: the warehouse still sees that
    # something happened, and the gap is visible rather than silent.
    return alert.alert_type


def _to_summary(alert: Alert, batch_ref: str | None) -> AlertSummary:
    return AlertSummary(
        alert_id=alert.alert_id,
        alert_type=alert.alert_type,
        alert_status=alert.alert_status,
        message=_message(alert, batch_ref),
        batch_id=alert.batch_id,
        batch_ref=batch_ref,
        created_at=alert.created_at,
        resolved_at=alert.resolved_at,
    )


async def _resolve_warehouse(session: AsyncSession, warehouse_ref: str) -> Warehouse:
    warehouse = await session.scalar(
        select(Warehouse).where(Warehouse.warehouse_ref == warehouse_ref)
    )
    if warehouse is None:
        raise WarehouseRefUnknownError(warehouse_ref)
    return warehouse


async def list_alerts(
    session: AsyncSession,
    warehouse_ref: str,
    status: AlertStatusFilter = AlertStatusFilter.ACTIVE,
    limit: int = 50,
) -> AlertList:
    """A warehouse's alerts, newest first.

    Open ones by default: the question a warehouse asks in the morning is what
    is still wrong, not everything that ever was. `status=all` serves the
    quality page, which shades resolved periods on its curve.
    """
    warehouse = await _resolve_warehouse(session, warehouse_ref)

    query = (
        select(Alert, Batch.batch_ref)
        .outerjoin(Batch, Batch.batch_id == Alert.batch_id)
        .where(Alert.warehouse_id == warehouse.warehouse_id)
        .order_by(Alert.created_at.desc())
        .limit(limit)
    )
    if status is not AlertStatusFilter.ALL:
        query = query.where(Alert.alert_status == status.value)

    rows = (await session.execute(query)).all()

    active_count = await session.scalar(
        select(func.count())
        .select_from(Alert)
        .where(
            Alert.warehouse_id == warehouse.warehouse_id,
            Alert.alert_status == ACTIVE,
        )
    )

    return AlertList(
        alerts=[_to_summary(alert, batch_ref) for alert, batch_ref in rows],
        active_count=active_count or 0,
    )


async def resolve_alert(
    session: AsyncSession,
    alert_id: uuid.UUID,
    warehouse_ref: str,
    now: datetime | None = None,
) -> AlertSummary:
    """Close an alert, and with it re-open the room's ability to raise another.

    The warehouse is part of the lookup, not a check made afterwards: an alert
    belonging to another warehouse answers `404` rather than `403`, so the route
    never confirms that an id exists to someone who has no business knowing.

    Idempotent. Resolving an already-resolved alert returns it unchanged rather
    than moving its timestamp — the question `resolvedAt` answers is "when did
    this stop", and a second click must not rewrite that.

    Deliberately does not touch `batches.is_compliant`. A room being repaired
    does not clear the batch that spent a night out of band; lifting that flag
    is a judgement about the coffee, made on the batch, not a side effect of
    acknowledging the room.
    """
    warehouse = await _resolve_warehouse(session, warehouse_ref)

    row = (
        await session.execute(
            select(Alert, Batch.batch_ref)
            .outerjoin(Batch, Batch.batch_id == Alert.batch_id)
            .where(
                Alert.alert_id == alert_id,
                Alert.warehouse_id == warehouse.warehouse_id,
            )
        )
    ).first()

    if row is None:
        raise AlertNotFoundError(alert_id)

    alert, batch_ref = row

    if alert.alert_status == ACTIVE:
        alert.alert_status = RESOLVED
        alert.resolved_at = now or datetime.now(UTC)
        await session.commit()
        await session.refresh(alert)

    return _to_summary(alert, batch_ref)
