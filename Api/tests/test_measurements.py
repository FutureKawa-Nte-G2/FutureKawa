"""The endpoint head office pulls: GET /api/measurements.

The contract is theirs, and it fails silently when broken — a wrong field name
lands as 0, a wrong date format is swallowed by their catch-all. So the wire
format is asserted on the raw JSON, not on the Python model.
"""

from datetime import UTC, datetime, timedelta
from decimal import Decimal

import pytest
from httpx import AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Measurement, Sensor, Warehouse
from app.services.measurements import daily_aggregate, previous_utc_day
from tests.conftest import SENSOR_ID, WAREHOUSE_ID, WAREHOUSE_REF

pytestmark = pytest.mark.asyncio

YESTERDAY = datetime.now(UTC) - timedelta(days=1)


def reading(hour: int, temp: str, humidity: str, sensor_id=SENSOR_ID) -> Measurement:
    """One reading placed on yesterday, the day the endpoint serves."""
    return Measurement(
        sensor_id=sensor_id,
        meas_date=YESTERDAY.replace(
            hour=hour, minute=0, second=0, microsecond=0
        ),
        meas_temp=Decimal(temp),
        meas_humidity=Decimal(humidity),
    )


async def test_aggregates_yesterday(session: AsyncSession) -> None:
    session.add_all(
        [
            reading(2, "20.00", "50.00"),
            reading(10, "24.00", "60.00"),
            reading(18, "22.00", "58.00"),
        ]
    )
    await session.commit()

    result = await daily_aggregate(session)

    assert result is not None
    assert result.avg_temp == Decimal("22.00")
    assert result.max_temp == Decimal("24.00")
    assert result.min_temp == Decimal("20.00")
    assert result.avg_humidity == Decimal("56.00")
    assert result.max_humidity == Decimal("60.00")
    assert result.min_humidity == Decimal("50.00")
    assert result.meas_date == previous_utc_day()


async def test_ignores_today_and_the_day_before_yesterday(
    session: AsyncSession,
) -> None:
    """The window is one day wide, closed on both ends.

    Serving the day in progress would freeze a partial average forever, since
    head office skips a date it already stored.
    """
    now = datetime.now(UTC)
    session.add_all(
        [
            reading(12, "20.00", "50.00"),
            Measurement(
                sensor_id=SENSOR_ID,
                meas_date=now.replace(hour=12),
                meas_temp=Decimal("99.00"),
                meas_humidity=Decimal("99.00"),
            ),
            Measurement(
                sensor_id=SENSOR_ID,
                meas_date=(now - timedelta(days=2)).replace(hour=12),
                meas_temp=Decimal("1.00"),
                meas_humidity=Decimal("1.00"),
            ),
        ]
    )
    await session.commit()

    result = await daily_aggregate(session)

    assert result is not None
    assert result.max_temp == Decimal("20.00")
    assert result.min_temp == Decimal("20.00")


async def test_ignores_inactive_sensors(session: AsyncSession) -> None:
    """A decommissioned sensor left in place must not drag the averages."""
    retired = Sensor(
        warehouse_id=WAREHOUSE_ID, code="BR-SEN-OFF", is_active=False
    )
    session.add(retired)
    await session.commit()

    session.add_all(
        [
            reading(9, "20.00", "50.00"),
            reading(9, "40.00", "90.00", sensor_id=retired.sensor_id),
        ]
    )
    await session.commit()

    result = await daily_aggregate(session)

    assert result is not None
    assert result.max_temp == Decimal("20.00")


async def test_rounds_to_two_decimals(session: AsyncSession) -> None:
    """avg() widens the scale on PostgreSQL and returns a float on SQLite."""
    session.add_all([reading(1, "20.00", "50.00"), reading(2, "21.00", "51.00")])
    await session.commit()

    result = await daily_aggregate(session)

    assert result is not None
    assert result.avg_temp == Decimal("20.50")
    assert result.avg_temp.as_tuple().exponent == -2


async def test_filters_on_warehouse_ref(session: AsyncSession) -> None:
    """The filter head office does not send yet, ready for the day it does."""
    other = Warehouse(
        country_id=(await session.get(Warehouse, WAREHOUSE_ID)).country_id,
        warehouse_name="Autre",
        warehouse_ref="BR-ENT-02",
    )
    session.add(other)
    await session.commit()
    elsewhere = Sensor(warehouse_id=other.warehouse_id, code="BR-SEN-02")
    session.add(elsewhere)
    await session.commit()

    session.add_all(
        [
            reading(9, "20.00", "50.00"),
            reading(9, "40.00", "90.00", sensor_id=elsewhere.sensor_id),
        ]
    )
    await session.commit()

    both = await daily_aggregate(session)
    assert both is not None and both.max_temp == Decimal("40.00")

    filtered = await daily_aggregate(session, warehouse_ref=WAREHOUSE_REF)
    assert filtered is not None and filtered.max_temp == Decimal("20.00")


async def test_wire_format(client: AsyncClient, session: AsyncSession) -> None:
    """Asserted on the raw JSON: a wrong name lands at 0 without an error."""
    session.add_all([reading(8, "22.50", "60.50"), reading(20, "28.10", "64.30")])
    await session.commit()

    response = await client.get("/api/measurements")

    assert response.status_code == 200
    body = response.json()
    assert set(body) == {
        "avgTemp",
        "maxTemp",
        "minTemp",
        "avgHumidity",
        "maxHumidity",
        "minHumidity",
        "measDate",
    }
    assert body["measDate"] == previous_utc_day().isoformat()
    assert len(body["measDate"]) == 10
    assert body["maxTemp"] == 28.10
    assert body["minTemp"] == 22.50

    # Their `decimal` refuses a quoted number: `System.Text.Json` is not given
    # NumberHandling.AllowReadingFromString, so `"28.10"` raises a
    # JsonException they swallow. Verified against their DTO and their options.
    assert all(
        isinstance(body[field], (int, float))
        for field in body
        if field != "measDate"
    )


async def test_no_reading_returns_200_and_a_null_body(client: AsyncClient) -> None:
    """`204` and a null date both raise on their side; `null` skips cleanly."""
    response = await client.get("/api/measurements")

    assert response.status_code == 200
    assert response.json() is None
