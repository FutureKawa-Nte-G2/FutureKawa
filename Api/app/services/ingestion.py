"""Consumer 1: storing readings.

A sensor belongs to a room and publishes even when no batch is there; storing
everything would fill `measurements` with readings tied to nothing. A reading
is kept only if the sensor was assigned to a batch when it measured (US #30),
judged on the reading's date so a message delayed by the broker still counts.
"""

import uuid
from dataclasses import dataclass
from datetime import UTC, datetime
from decimal import Decimal

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import Measurement, Sensor, SensorAssignment


@dataclass(frozen=True)
class Reading:
    """Identified by sensor `code`, never by UUID: the firmware does not know ours."""

    sensor_code: str
    measured_at: datetime
    temperature: Decimal
    humidity: Decimal


async def persist_reading(session: AsyncSession, reading: Reading) -> uuid.UUID | None:
    """Returns `None` for a reading deliberately skipped, which is not an error.

    Idempotent because QoS 1 redelivers: the `(sensor_id, meas_date)`
    constraint rejects the replay.
    """
    sensor = await session.scalar(
        select(Sensor).where(Sensor.code == reading.sensor_code, Sensor.is_active)
    )
    if sensor is None:
        return None

    measured_at = _as_naive_utc(reading.measured_at)

    assignment = await session.scalar(
        select(SensorAssignment).where(
            SensorAssignment.sensor_id == sensor.sensor_id,
            SensorAssignment.assigned_at <= measured_at,
            _released_after(measured_at),
        )
    )
    if assignment is None:
        return None

    measurement = Measurement(
        measurement_id=uuid.uuid4(),
        sensor_id=sensor.sensor_id,
        meas_date=measured_at,
        meas_temp=reading.temperature,
        meas_humidity=reading.humidity,
    )
    session.add(measurement)

    try:
        await session.commit()
    except IntegrityError:
        # Redelivered message: raising would only loop the redelivery.
        await session.rollback()
        return None

    return measurement.measurement_id


def _released_after(moment: datetime):
    """Assignment still open, or closed after the reading."""
    return (SensorAssignment.released_at.is_(None)) | (
        SensorAssignment.released_at > moment
    )


def _as_naive_utc(value: datetime) -> datetime:
    """SQLite returns naive timestamps, PostgreSQL aware ones: comparing both raises."""
    if value.tzinfo is None:
        return value
    return value.astimezone(UTC).replace(tzinfo=None)
