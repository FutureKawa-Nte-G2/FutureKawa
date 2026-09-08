"""What the rest of the application is allowed to assume.

These are not tests of a feature: they pin the pieces every feature branch
builds on — the schema creates, the app boots, the error shape is uniform, and
the database seam fails closed when it is not configured.
"""

import uuid
from datetime import UTC, datetime
from decimal import Decimal

import pytest
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError

from app.db import DATABASE_URL_ENV_VAR, get_engine
from app.models import (
    ALERT_STATUSES,
    ALERT_TYPES,
    BATCH_STATUSES,
    QUALITY_GRADES,
    Alert,
    Base,
    Batch,
    Measurement,
    Sensor,
)
from tests.conftest import (
    BATCH_REF,
    SENSOR_ID,
    WAREHOUSE_ID,
    reference_country,
)

pytestmark = pytest.mark.asyncio


# --- the schema holds ------------------------------------------------------


async def test_should_create_every_table_of_the_mld(empty_session):
    """The 12 tables of mld_warehouse.puml, no more and no less."""
    assert set(Base.metadata.tables) == {
        "countries",
        "farms",
        "warehouses",
        "users",
        "batches",
        "sensors",
        "sensor_assignments",
        "measurements",
        "alerts",
        "orders",
        "order_lines",
        "order_batches",
    }


async def test_should_seed_a_country_where_every_join_resolves(session):
    batch = await session.scalar(select(Batch).where(Batch.batch_ref == BATCH_REF))

    assert batch.warehouse_id == WAREHOUSE_ID
    assert batch.batch_status in BATCH_STATUSES
    assert batch.quality_grade in QUALITY_GRADES


async def test_should_generate_a_uuid_when_none_is_given(empty_session):
    """Callers insert without an id: the default has to produce one."""
    empty_session.add_all(reference_country())
    await empty_session.commit()

    sensor = Sensor(warehouse_id=WAREHOUSE_ID, code="BR-SEN-02")
    empty_session.add(sensor)
    await empty_session.commit()

    assert isinstance(sensor.sensor_id, uuid.UUID)


# --- the constraints that carry a rule -------------------------------------


async def test_should_refuse_a_duplicated_batch_ref(session):
    """What stops a replayed ERP reception file from creating a batch twice."""
    session.add(
        Batch(
            warehouse_id=WAREHOUSE_ID,
            farm_id=(await session.scalar(select(Batch.farm_id))),
            batch_ref=BATCH_REF,
            stored_at=datetime.now(UTC).date(),
            quality_grade="B",
            batch_status="stored",
        )
    )

    with pytest.raises(IntegrityError):
        await session.commit()


async def test_should_refuse_two_measurements_for_one_sensor_at_one_instant(session):
    """Makes an MQTT message replay idempotent rather than duplicated."""
    taken_at = datetime(2026, 7, 30, 9, 0, tzinfo=UTC)
    for _ in range(2):
        session.add(
            Measurement(
                sensor_id=SENSOR_ID,
                meas_date=taken_at,
                meas_temp=Decimal("21.00"),
                meas_humidity=Decimal("56.00"),
            )
        )

    with pytest.raises(IntegrityError):
        await session.commit()


async def test_should_allow_two_sensors_to_report_at_the_same_instant(session):
    """The MLD marks meas_date alone as UNIQUE, which would forbid this. Two
    devices on one broker do it constantly, so the constraint is read as
    (sensor_id, meas_date).
    """
    taken_at = datetime(2026, 7, 30, 9, 0, tzinfo=UTC)
    other_sensor = Sensor(warehouse_id=WAREHOUSE_ID, code="BR-SEN-02")
    session.add(other_sensor)
    await session.flush()

    session.add_all(
        [
            Measurement(
                sensor_id=SENSOR_ID,
                meas_date=taken_at,
                meas_temp=Decimal("21.00"),
                meas_humidity=Decimal("56.00"),
            ),
            Measurement(
                sensor_id=other_sensor.sensor_id,
                meas_date=taken_at,
                meas_temp=Decimal("22.00"),
                meas_humidity=Decimal("57.00"),
            ),
        ]
    )

    await session.commit()

    assert len((await session.scalars(select(Measurement))).all()) == 2


def _condition_alert() -> Alert:
    return Alert(
        warehouse_id=WAREHOUSE_ID,
        alert_type="condition",
        alert_status="active",
        created_at=datetime.now(UTC),
    )


async def test_should_refuse_a_second_active_condition_alert_per_warehouse(session):
    """The anti-spam of the evaluation consumer is a data rule: two readings
    evaluated at once would both pass a read-then-write check.
    """
    session.add(_condition_alert())
    await session.commit()

    session.add(_condition_alert())
    with pytest.raises(IntegrityError):
        await session.commit()


async def test_should_allow_a_new_alert_once_the_previous_one_is_resolved(session):
    resolved = _condition_alert()
    resolved.alert_status = "resolved"
    resolved.resolved_at = datetime.now(UTC)
    session.add(resolved)
    await session.commit()

    session.add(_condition_alert())
    await session.commit()

    assert len((await session.scalars(select(Alert))).all()) == 2


async def test_should_let_an_expiration_alert_coexist_with_a_condition_alert(session):
    """The index is partial on type: a warehouse under a condition alert can
    still have a batch expire.
    """
    session.add(_condition_alert())
    await session.commit()

    session.add(
        Alert(
            warehouse_id=WAREHOUSE_ID,
            batch_id=(await session.scalar(select(Batch.batch_id))),
            alert_type="expiration",
            alert_status="active",
            created_at=datetime.now(UTC),
        )
    )
    await session.commit()

    assert len((await session.scalars(select(Alert))).all()) == 2


# --- vocabulary shared with head office ------------------------------------


async def test_should_pin_the_enums_head_office_owns():
    """Head office types these as C# enums. A value outside them reaches the
    siège with no mapping, so the two lists are pinned here rather than spelled
    out again in every service.
    """
    assert QUALITY_GRADES == ("A", "B", "C")
    assert BATCH_STATUSES == ("stored", "shipped", "delivered", "expired")
    assert ALERT_TYPES == ("condition", "expiration")
    assert ALERT_STATUSES == ("active", "resolved")


# --- the application boots -------------------------------------------------


async def test_should_answer_health_without_touching_the_database(client):
    response = await client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


async def test_should_expose_an_openapi_document(client):
    response = await client.get("/openapi.json")

    assert response.status_code == 200
    assert response.json()["info"]["title"] == "FutureKawa — API pays"


async def test_should_answer_404_in_the_normalised_shape(client):
    response = await client.get("/api/nothing-here")

    assert response.status_code == 404


# --- the database seam fails closed ----------------------------------------


async def test_should_refuse_to_build_an_engine_without_a_database_url(monkeypatch):
    """An unset variable is a deployment mistake; it should look like one
    rather than silently falling back to something local.
    """
    monkeypatch.delenv(DATABASE_URL_ENV_VAR, raising=False)

    with pytest.raises(RuntimeError, match=DATABASE_URL_ENV_VAR):
        get_engine()
