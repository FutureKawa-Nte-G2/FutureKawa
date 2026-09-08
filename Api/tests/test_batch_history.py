"""GET /api/batches/{id}/measurements — the curve of one batch.

What these tests pin down is the sensor window. A sensor belongs to a room, so
the same device measures whatever batch happens to be under it: readings only
belong to a batch while it was assigned, and getting that wrong serves every
batch of a warehouse the same curve.
"""

import uuid
from datetime import UTC, datetime
from decimal import Decimal

import pytest
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Alert, Measurement, Sensor, SensorAssignment
from tests.conftest import (
    BATCH_ID,
    BATCH_REF,
    NOMINAL_TEMP,
    SENSOR_ID,
    TOLERANCE_TEMP,
    WAREHOUSE_ID,
)

pytestmark = pytest.mark.asyncio

URL = f"/api/batches/{BATCH_ID}/measurements"

OTHER_SENSOR_ID = uuid.UUID("00000000-0000-0000-0000-0000000000a2")


def _at(day: int, hour: int) -> datetime:
    """A reading time in August 2026, naive UTC like SQLite stores it."""
    return datetime(2026, 8, day, hour, 0, 0)


def _reading(sensor_id: uuid.UUID, when: datetime, temp: str, humidity: str) -> Measurement:
    return Measurement(
        measurement_id=uuid.uuid4(),
        sensor_id=sensor_id,
        meas_date=when,
        meas_temp=Decimal(temp),
        meas_humidity=Decimal(humidity),
    )


async def _assign(session: AsyncSession, start: datetime, end: datetime | None) -> None:
    session.add(
        SensorAssignment(
            sensor_assignment_id=uuid.uuid4(),
            sensor_id=SENSOR_ID,
            batch_id=BATCH_ID,
            assigned_at=start,
            released_at=end,
        )
    )
    await session.commit()


# --- the window is what makes a reading belong to a batch -------------------


async def test_should_aggregate_one_point_per_day(session, client):
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add_all(
        [
            _reading(SENSOR_ID, _at(10, 6), "19.00", "50.00"),
            _reading(SENSOR_ID, _at(10, 18), "23.00", "56.00"),
            _reading(SENSOR_ID, _at(11, 9), "21.00", "52.00"),
        ]
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert [p["day"] for p in body["dailyPoints"]] == ["2026-08-10", "2026-08-11"]
    first = body["dailyPoints"][0]
    # min and max carry the excursions an average alone would flatten away.
    assert (first["minTemp"], first["avgTemp"], first["maxTemp"]) == (19.0, 21.0, 23.0)


async def test_should_ignore_readings_taken_before_the_sensor_was_assigned(session, client):
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add_all(
        [
            _reading(SENSOR_ID, _at(9, 23), "99.00", "99.00"),  # veille, hors fenêtre
            _reading(SENSOR_ID, _at(10, 6), "20.00", "55.00"),
        ]
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert [p["day"] for p in body["dailyPoints"]] == ["2026-08-10"]
    assert body["dailyPoints"][0]["maxTemp"] == 20.0


async def test_should_ignore_readings_taken_after_the_sensor_was_released(session, client):
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add_all(
        [
            _reading(SENSOR_ID, _at(11, 6), "20.00", "55.00"),
            _reading(SENSOR_ID, _at(13, 6), "99.00", "99.00"),  # après libération
        ]
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert [p["day"] for p in body["dailyPoints"]] == ["2026-08-11"]


async def test_should_ignore_a_sensor_that_was_never_assigned_to_this_batch(session, client):
    """The decisive case: two devices in the same room, one batch."""
    session.add(Sensor(sensor_id=OTHER_SENSOR_ID, warehouse_id=WAREHOUSE_ID, code="BR-SEN-02"))
    await session.commit()
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add_all(
        [
            _reading(SENSOR_ID, _at(10, 6), "20.00", "55.00"),
            _reading(OTHER_SENSOR_ID, _at(10, 7), "99.00", "99.00"),
        ]
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert body["dailyPoints"][0]["maxTemp"] == 20.0


async def test_should_keep_serving_a_batch_whose_sensor_is_still_assigned(session, client):
    """released_at NULL means the window runs to now, not that it is empty."""
    await _assign(session, _at(10, 0), None)
    session.add(_reading(SENSOR_ID, _at(10, 6), "20.00", "55.00"))
    await session.commit()

    body = (await client.get(URL)).json()

    assert len(body["dailyPoints"]) == 1
    assert body["windowTo"] is not None


# --- the two granularities --------------------------------------------------


async def test_should_return_individual_readings_when_raw(session, client):
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add_all(
        [
            _reading(SENSOR_ID, _at(10, 6), "19.00", "50.00"),
            _reading(SENSOR_ID, _at(10, 18), "23.00", "56.00"),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"granularity": "raw"})).json()

    assert body["granularity"] == "raw"
    assert [p["temp"] for p in body["rawPoints"]] == [19.0, 23.0]
    assert body["dailyPoints"] == []


async def test_should_refuse_a_raw_window_wider_than_a_week(session, client):
    """A year of 5-minute readings is ~105 000 points: daily exists for that."""
    await _assign(session, _at(1, 0), _at(31, 0))

    response = await client.get(
        URL,
        params={
            "granularity": "raw",
            "from": "2026-08-01T00:00:00",
            "to": "2026-08-31T00:00:00",
        },
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "window_too_wide"


async def test_should_serve_the_last_week_when_raw_is_called_without_a_window(session, client):
    """A caller with no window gets the tail, not a refusal."""
    await _assign(session, _at(1, 0), _at(31, 0))

    response = await client.get(URL, params={"granularity": "raw"})

    assert response.status_code == 200
    body = response.json()
    served = datetime.fromisoformat(body["windowTo"]) - datetime.fromisoformat(body["windowFrom"])
    assert served.days <= 7


async def test_should_aggregate_over_a_long_period_when_daily(session, client):
    """The same span daily is served without complaint."""
    await _assign(session, _at(1, 0), _at(31, 0))
    session.add_all(
        [_reading(SENSOR_ID, _at(day, 12), "20.00", "55.00") for day in (1, 15, 30)]
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert len(body["dailyPoints"]) == 3


# --- what the frontend needs alongside the points ---------------------------


async def test_should_return_the_country_band_not_a_ceiling(session, client):
    """nominal ± tolerance, as the MCD, the MLD and head office's Country all say."""
    await _assign(session, _at(10, 0), None)

    body = (await client.get(URL)).json()

    assert body["thresholds"] == {
        "nominalTemp": float(NOMINAL_TEMP),
        "toleranceTemp": float(TOLERANCE_TEMP),
        "nominalHumidity": 55.0,
        "toleranceHumidity": 5.0,
    }


async def test_should_return_the_room_condition_alert_that_marks_the_period(session, client):
    """A condition alert carries no batch_id, yet it is the one to shade."""
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add(
        Alert(
            alert_id=uuid.uuid4(),
            warehouse_id=WAREHOUSE_ID,
            batch_id=None,
            alert_type="condition",
            alert_status="active",
            created_at=_at(10, 8),
            resolved_at=_at(10, 20),
        )
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert len(body["alerts"]) == 1
    assert body["alerts"][0]["alertType"] == "condition"
    assert body["alerts"][0]["concernsBatch"] is False


async def test_should_mark_an_alert_raised_on_this_batch_as_its_own(session, client):
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add(
        Alert(
            alert_id=uuid.uuid4(),
            warehouse_id=WAREHOUSE_ID,
            batch_id=BATCH_ID,
            alert_type="expiration",
            alert_status="active",
            created_at=_at(11, 8),
            resolved_at=None,
        )
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert body["alerts"][0]["concernsBatch"] is True


async def test_should_leave_out_an_alert_that_does_not_overlap_the_window(session, client):
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add(
        Alert(
            alert_id=uuid.uuid4(),
            warehouse_id=WAREHOUSE_ID,
            batch_id=None,
            alert_type="condition",
            alert_status="resolved",
            created_at=_at(1, 8),
            resolved_at=_at(1, 20),
        )
    )
    await session.commit()

    body = (await client.get(URL)).json()

    assert body["alerts"] == []


# --- edges ------------------------------------------------------------------


async def test_should_answer_404_for_a_batch_that_does_not_exist(client):
    response = await client.get(f"/api/batches/{uuid.uuid4()}/measurements")

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "batch_not_found"


async def test_should_answer_200_with_empty_series_for_a_batch_without_a_sensor(session, client):
    """Received but not yet equipped is a legitimate state, not an error."""
    response = await client.get(URL)

    assert response.status_code == 200
    body = response.json()
    assert body["batchRef"] == BATCH_REF
    assert body["dailyPoints"] == []
    assert body["windowFrom"] is None
    # The band is served anyway: the chart draws its lines before any point.
    assert body["thresholds"]["nominalTemp"] == float(NOMINAL_TEMP)


async def test_should_keep_serving_a_shipped_batch(session, client):
    """A quality review happens after the fact — nothing filters on status."""
    from app.models import Batch
    from sqlalchemy import select

    batch = await session.scalar(select(Batch).where(Batch.batch_id == BATCH_ID))
    batch.batch_status = "shipped"
    await session.commit()
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add(_reading(SENSOR_ID, _at(10, 6), "20.00", "55.00"))
    await session.commit()

    body = (await client.get(URL)).json()

    assert len(body["dailyPoints"]) == 1


async def test_should_stamp_utc_on_every_timestamp_it_serves(session, client):
    """Naive on one engine, aware on the other, would shift a browser's axis."""
    await _assign(session, _at(10, 0), _at(12, 0))
    session.add(_reading(SENSOR_ID, _at(10, 6), "20.00", "55.00"))
    await session.commit()

    body = (await client.get(URL, params={"granularity": "raw"})).json()

    assert body["windowFrom"].endswith("Z") or "+00:00" in body["windowFrom"]
    assert body["windowTo"].endswith("Z") or "+00:00" in body["windowTo"]
    served = body["rawPoints"][0]["measDate"]
    assert served.endswith("Z") or "+00:00" in served


async def test_should_serve_camel_case_on_the_wire(session, client):
    """The reader is head office's .NET relay: a different name binds to zero."""
    await _assign(session, _at(10, 0), None)

    body = (await client.get(URL)).json()

    for key in ("batchRef", "dailyPoints", "rawPoints", "windowFrom", "windowTo", "thresholds"):
        assert key in body, key
    assert "batch_ref" not in body
