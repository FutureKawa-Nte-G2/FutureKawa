from typing import Annotated

from fastapi import APIRouter, Depends, Query
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemas.measurement import MeasurementAggregate
from app.services.measurements import daily_aggregate

router = APIRouter(prefix="/api", tags=["measurements"])


@router.get("/measurements", response_model=MeasurementAggregate | None)
async def read_measurements(
    session: Annotated[AsyncSession, Depends(get_session)],
    warehouse_ref: Annotated[str | None, Query(max_length=64)] = None,
) -> MeasurementAggregate | None:
    """Yesterday's aggregate for head office.

    Deliberately unauthenticated. Their client sends no header at all: adding a
    key here would earn a 401, `EnsureSuccessStatusCode` would raise, the
    exception would be swallowed and the warehouse skipped — with one log line
    on their side and nothing at all on ours. Auth has to be agreed and shipped
    on both sides in the same move.

    No readings yields `200` with a `null` body, never `204` and never an object
    with a null date: both of those raise a JsonException on their side, which
    costs them an error log on every quiet cycle. `null` deserialises cleanly
    and skips the warehouse without noise.
    """
    return await daily_aggregate(session, warehouse_ref=warehouse_ref)
