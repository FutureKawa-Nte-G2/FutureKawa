"""Daily aggregation of sensor readings, as pulled by head office.

Head office calls one URL per cycle and stores one row per (warehouse, date),
skipping any date it already holds. Two consequences shape everything here: the
day we serve must never change afterwards, and an empty answer must stay cheap.
"""

from datetime import UTC, date, datetime, time, timedelta
from decimal import ROUND_HALF_UP, Decimal

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Measurement, Sensor, Warehouse
from app.schemas.measurement import MeasurementAggregate


def previous_utc_day(now: datetime | None = None) -> date:
    """The last day that can no longer receive a reading.

    Serving the day in progress would freeze whatever partial figures the first
    pull of the day happened to see: head office stores that row and skips the
    date forever after. Yesterday is final, which makes their idempotency free
    rather than a source of silently wrong averages.

    The cut is UTC. `countries` carries no timezone, and a Brazilian day
    (UTC-3) would split the night across two UTC days — which moves the min and
    max. Documented as a simplification, not as a claim that it is equivalent.
    """
    return ((now or datetime.now(UTC)) - timedelta(days=1)).date()


def _two_decimals(value: Decimal | float) -> Decimal:
    """Bring an aggregate back to the 2 decimals of the contract.

    `avg()` widens the scale on PostgreSQL and hands back a float on SQLite;
    both have to reach the wire as the same 2-decimal number.
    """
    return Decimal(str(value)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


async def daily_aggregate(
    session: AsyncSession,
    warehouse_ref: str | None = None,
    now: datetime | None = None,
) -> MeasurementAggregate | None:
    """Aggregate one UTC day of readings, or None when there are none.

    Only active sensors count: a decommissioned one left in place would keep
    dragging the averages of a warehouse it no longer measures.

    `warehouse_ref` is optional because head office does not send it today —
    `FetchMeasurementsAsync` receives the warehouse id and never puts it in the
    URL, so a single URL serves every warehouse. Accepting the filter now means
    the day they add it, nothing changes on our side.
    """
    day = previous_utc_day(now)
    start = datetime.combine(day, time.min, tzinfo=UTC)
    end = start + timedelta(days=1)

    query = (
        select(
            func.avg(Measurement.meas_temp),
            func.max(Measurement.meas_temp),
            func.min(Measurement.meas_temp),
            func.avg(Measurement.meas_humidity),
            func.max(Measurement.meas_humidity),
            func.min(Measurement.meas_humidity),
        )
        .join(Sensor, Sensor.sensor_id == Measurement.sensor_id)
        .where(
            Sensor.is_active,
            Measurement.meas_date >= start,
            Measurement.meas_date < end,
        )
    )

    if warehouse_ref is not None:
        query = query.join(
            Warehouse, Warehouse.warehouse_id == Sensor.warehouse_id
        ).where(Warehouse.warehouse_ref == warehouse_ref)

    row = (await session.execute(query)).one()

    # An aggregate over zero rows is a row of NULLs, not an empty result set.
    if row[0] is None:
        return None

    avg_temp, max_temp, min_temp, avg_humidity, max_humidity, min_humidity = row
    return MeasurementAggregate(
        avg_temp=_two_decimals(avg_temp),
        max_temp=_two_decimals(max_temp),
        min_temp=_two_decimals(min_temp),
        avg_humidity=_two_decimals(avg_humidity),
        max_humidity=_two_decimals(max_humidity),
        min_humidity=_two_decimals(min_humidity),
        meas_date=day,
    )
