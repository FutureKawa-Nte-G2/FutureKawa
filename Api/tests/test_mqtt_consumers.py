"""Les deux consumers abonnés au broker (US #32).

Ce que font les deux consumers une fois le message décodé. Le décodage et la
règle de bande, qui ne touchent pas la base, sont dans `test_mqtt_payload.py`.

Aucun broker n'est monté : les services reçoivent un relevé déjà décodé,
précisément pour être vérifiables sans Mosquitto, qui n'est pas encore
configuré (#31). Le test d'intégration bout en bout « message MQTT → écriture
en base » exigé par la DoD reste à faire le jour où le broker existe.
"""

import uuid
from datetime import datetime
from decimal import Decimal

import pytest
from sqlalchemy import func, select

from app.models import Alert, Batch, Notification, Sensor, SensorAssignment
from app.services.ingestion import Reading, persist_reading
from app.services.quality import evaluate_reading
from tests.conftest import (
    BATCH_ID,
    SENSOR_CODE,
    SENSOR_ID,
    WAREHOUSE_ID,
)

pytestmark = pytest.mark.asyncio


def _at(day: int, hour: int = 12) -> datetime:
    return datetime(2026, 8, day, hour, 0, 0)


def _reading(temp: str = "20.00", humidity: str = "55.00", day: int = 10) -> Reading:
    return Reading(
        sensor_code=SENSOR_CODE,
        measured_at=_at(day),
        temperature=Decimal(temp),
        humidity=Decimal(humidity),
    )


async def _assign(session, start=None, end=None) -> None:
    session.add(
        SensorAssignment(
            sensor_assignment_id=uuid.uuid4(),
            sensor_id=SENSOR_ID,
            batch_id=BATCH_ID,
            assigned_at=start or _at(1),
            released_at=end,
        )
    )
    await session.commit()


# --- consumer 1 : écriture --------------------------------------------------


async def test_should_write_a_reading_from_an_assigned_sensor(session):
    await _assign(session)

    written = await persist_reading(session, _reading())

    assert written is not None
    assert await session.scalar(select(func.count()).select_from(Alert)) == 0


async def test_should_ignore_a_reading_from_a_sensor_with_no_assignment(session):
    """Un capteur au repos publie quand même : ce bruit ne doit pas être stocké."""
    written = await persist_reading(session, _reading())

    assert written is None


async def test_should_ignore_a_reading_taken_after_the_sensor_was_released(session):
    await _assign(session, start=_at(1), end=_at(5))

    assert await persist_reading(session, _reading(day=10)) is None


async def test_should_judge_a_late_message_on_when_it_was_taken(session):
    """Le broker retarde ; le relevé se juge à sa date, pas à sa réception."""
    await _assign(session, start=_at(1), end=_at(5))

    assert await persist_reading(session, _reading(day=3)) is not None


async def test_should_ignore_a_reading_from_an_unknown_sensor(session):
    await _assign(session)
    unknown = Reading(
        sensor_code="NEXISTE-PAS",
        measured_at=_at(10),
        temperature=Decimal("20"),
        humidity=Decimal("55"),
    )

    assert await persist_reading(session, unknown) is None


async def test_should_ignore_a_reading_from_a_deactivated_sensor(session):
    sensor = await session.get(Sensor, SENSOR_ID)
    sensor.is_active = False
    await session.commit()
    await _assign(session)

    assert await persist_reading(session, _reading()) is None


async def test_should_not_duplicate_a_redelivered_message(session):
    """QoS 1 redélivre : rejouer ne doit pas créer une seconde ligne."""
    await _assign(session)
    reading = _reading()

    first = await persist_reading(session, reading)
    second = await persist_reading(session, reading)

    assert first is not None
    assert second is None


# --- consumer 2 : évaluation ------------------------------------------------


async def test_should_leave_a_compliant_batch_alone_when_the_reading_is_in_band(session):
    await _assign(session)

    result = await evaluate_reading(session, _reading("21.00", "56.00"))

    assert result.within_band is True
    assert result.alert_created is False
    batch = await session.get(Batch, BATCH_ID)
    assert batch.is_compliant is True


async def test_should_flip_the_batch_and_raise_alert_and_notification_together(session):
    """La DoD : bascule + ALERT + NOTIFICATION en une seule fois."""
    await _assign(session)

    result = await evaluate_reading(session, _reading("31.00", "55.00"))

    assert result.compliance_flipped is True
    assert result.alert_created is True
    assert result.notification_created is True

    batch = await session.get(Batch, BATCH_ID)
    assert batch.is_compliant is False

    alert = await session.scalar(select(Alert))
    assert (alert.alert_type, alert.alert_status) == ("condition", "active")

    notification = await session.scalar(select(Notification))
    assert notification.notification_type == "batch_non_compliant"
    assert notification.batch_id == BATCH_ID
    assert notification.warehouse_id == WAREHOUSE_ID


async def test_should_not_raise_a_second_alert_for_an_already_flagged_batch(session):
    """Anti-spam : sans ça, 288 notifications par jour pour un seul incident."""
    await _assign(session)

    await evaluate_reading(session, _reading("31.00", "55.00", day=10))
    second = await evaluate_reading(session, _reading("32.00", "55.00", day=11))
    third = await evaluate_reading(session, _reading("33.00", "55.00", day=12))

    assert second.alert_created is False
    assert third.alert_created is False
    assert await session.scalar(select(func.count()).select_from(Alert)) == 1
    assert await session.scalar(select(func.count()).select_from(Notification)) == 1


async def test_should_not_restore_compliance_when_the_reading_comes_back_in_band(session):
    """La levée est une décision humaine, pas une conséquence mécanique."""
    await _assign(session)
    await evaluate_reading(session, _reading("31.00", "55.00", day=10))

    await evaluate_reading(session, _reading("20.00", "55.00", day=11))

    batch = await session.get(Batch, BATCH_ID)
    assert batch.is_compliant is False


async def test_should_evaluate_nothing_for_a_sensor_with_no_assignment(session):
    result = await evaluate_reading(session, _reading("31.00", "55.00"))

    assert result.batch_id is None
    assert await session.scalar(select(func.count()).select_from(Alert)) == 0


async def test_should_stay_independent_of_whether_the_reading_was_stored(session):
    """Les deux consumers sont indépendants : l'un peut agir sans l'autre."""
    await _assign(session)

    result = await evaluate_reading(session, _reading("31.00", "55.00"))

    assert result.alert_created is True
    # Aucune mesure écrite : personne n'a appelé le consumer de persistance.
    from app.models import Measurement

    assert await session.scalar(select(func.count()).select_from(Measurement)) == 0
