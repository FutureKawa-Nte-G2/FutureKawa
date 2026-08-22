from collections.abc import Sequence
from datetime import datetime

from sqlalchemy import Row, func, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import aliased

from app.models import Alert, Batch, Warehouse
from app.schemas.alert import AlertState


async def list_alerts(
    session: AsyncSession,
    since: datetime | None = None,
    state: AlertState | None = None,
) -> Sequence[Row]:
    """List the country's alerts, most recent first.

    Every alert is resolved to a warehouse reference, whichever of the two
    shapes it has: a `condition` alert points at its warehouse, an `expiration`
    alert points at a batch and reaches the warehouse through it. Head office
    cannot dispatch an alert it cannot attribute, so this join is not a
    convenience.

    `since` is exclusive, so head office can pass back the `createdAt` of the
    last alert it stored and get only what came after it.
    """
    warehouse_of_alert = aliased(Warehouse)
    warehouse_of_batch = aliased(Warehouse)

    query = (
        select(
            Alert.id,
            Alert.type,
            Alert.state,
            Alert.value,
            Alert.created_at,
            Alert.resolved_at,
            func.coalesce(
                warehouse_of_alert.external_ref, warehouse_of_batch.external_ref
            ).label("warehouse_ref"),
            Batch.batch_ref.label("batch_ref"),
        )
        .outerjoin(warehouse_of_alert, warehouse_of_alert.id == Alert.warehouse_id)
        .outerjoin(Batch, Batch.id == Alert.batch_id)
        .outerjoin(warehouse_of_batch, warehouse_of_batch.id == Batch.warehouse_id)
    )

    if since is not None:
        query = query.where(Alert.created_at > since)

    if state is not None:
        query = query.where(Alert.state == state.value)

    # id as tie-breaker: created_at alone leaves rows sharing an instant in an
    # undefined order, which would make an incremental pull skip or repeat rows.
    query = query.order_by(Alert.created_at.desc(), Alert.id.desc())

    return (await session.execute(query)).all()
