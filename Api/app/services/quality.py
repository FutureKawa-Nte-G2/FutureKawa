"""Consumer 2: threshold evaluation.

Independent from the ingestion consumer, so a reading may raise an alert
without being stored, and the other way round. That is intended.
"""

import uuid
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from decimal import Decimal

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import (
    Alert,
    Batch,
    Country,
    Measurement,
    Notification,
    Sensor,
    SensorAssignment,
    Warehouse,
)
from app.services.head_office import AlertPush, Metric
from app.services.ingestion import Reading, _as_naive_utc, _released_after

# Beyond this, a linear drift between two readings is no longer credible.
MAX_INTERPOLATION_GAP = timedelta(minutes=15)


@dataclass(frozen=True)
class Band:
    """A band, not a ceiling: too cold damages coffee as much as too hot."""

    nominal: Decimal
    tolerance: Decimal

    def contains(self, value: Decimal) -> bool:
        return abs(value - self.nominal) <= self.tolerance

    def deviation_ratio(self, value: Decimal) -> Decimal:
        """Deviation in tolerances, so °C and % can be compared."""
        deviation = abs(value - self.nominal)
        if self.tolerance == 0:
            return Decimal("Infinity") if deviation else Decimal(0)
        return deviation / self.tolerance

    def limit_crossed(self, value: Decimal) -> Decimal:
        if value > self.nominal:
            return self.nominal + self.tolerance
        return self.nominal - self.tolerance


@dataclass(frozen=True)
class Sample:
    measured_at: datetime
    temperature: Decimal
    humidity: Decimal


@dataclass(frozen=True)
class Evaluation:
    batch_id: uuid.UUID | None = None
    within_band: bool = True
    alert_created: bool = False
    notification_created: bool = False
    compliance_flipped: bool = False
    # Sent by the caller: this module stays free of network calls.
    alert_push: AlertPush | None = None


def _bands(country: Country) -> tuple[Band, Band]:
    return (
        Band(country.nominal_temp, country.tolerance_temp),
        Band(country.nominal_humidity, country.tolerance_humidity),
    )


def is_within_band(
    temperature: Decimal, humidity: Decimal, country: Country
) -> bool:
    temp_band, humidity_band = _bands(country)
    return temp_band.contains(temperature) and humidity_band.contains(humidity)


def _out_of_band(sample: Sample, country: Country) -> tuple[bool, bool]:
    temp_band, humidity_band = _bands(country)
    return (
        not temp_band.contains(sample.temperature),
        not humidity_band.contains(sample.humidity),
    )


def select_breached_metric(
    current: Sample, previous: Sample | None, country: Country
) -> Metric | None:
    """The metric that crossed its limit first.

    Head office expects a single metric, while a condition alert covers the
    whole room. When both are out on the same reading, only the previous
    reading can tell which one drifted first.
    """
    temp_band, humidity_band = _bands(country)
    temp_out, humidity_out = _out_of_band(current, country)

    if not temp_out and not humidity_out:
        return None
    if temp_out != humidity_out:
        return "temperature" if temp_out else "humidity"

    if previous is None or current.measured_at - previous.measured_at > MAX_INTERPOLATION_GAP:
        return _largest_deviation(current, temp_band, humidity_band)

    previous_temp_out, previous_humidity_out = _out_of_band(previous, country)
    if previous_temp_out != previous_humidity_out:
        return "temperature" if previous_temp_out else "humidity"
    if previous_temp_out and previous_humidity_out:
        return _largest_deviation(current, temp_band, humidity_band)

    # Same interval for both metrics, so comparing fractions compares instants.
    temp_fraction = _crossing_fraction(temp_band, previous.temperature, current.temperature)
    humidity_fraction = _crossing_fraction(
        humidity_band, previous.humidity, current.humidity
    )
    if temp_fraction < humidity_fraction:
        return "temperature"
    if humidity_fraction < temp_fraction:
        return "humidity"
    return _largest_deviation(current, temp_band, humidity_band)


def _crossing_fraction(band: Band, previous: Decimal, current: Decimal) -> Decimal:
    # Never divides by zero: `previous` is in band and `current` is not.
    return (band.limit_crossed(current) - previous) / (current - previous)


def _largest_deviation(sample: Sample, temp_band: Band, humidity_band: Band) -> Metric:
    temp_ratio = temp_band.deviation_ratio(sample.temperature)
    humidity_ratio = humidity_band.deviation_ratio(sample.humidity)
    # Ties go to temperature for determinism only, not a business priority.
    return "humidity" if humidity_ratio > temp_ratio else "temperature"


async def _previous_sample(
    session: AsyncSession, sensor_code: str, measured_at: datetime
) -> Sample | None:
    """May be missing: the ingestion consumer runs independently of this one."""
    row = (
        await session.execute(
            select(Measurement.meas_date, Measurement.meas_temp, Measurement.meas_humidity)
            .join(Sensor, Sensor.sensor_id == Measurement.sensor_id)
            .where(
                Sensor.code == sensor_code,
                Measurement.meas_date < measured_at,
                Measurement.meas_date >= measured_at - MAX_INTERPOLATION_GAP,
            )
            .order_by(Measurement.meas_date.desc())
            .limit(1)
        )
    ).first()
    if row is None:
        return None
    return Sample(
        measured_at=_as_naive_utc(row.meas_date),
        temperature=row.meas_temp,
        humidity=row.meas_humidity,
    )


async def evaluate_reading(
    session: AsyncSession, reading: Reading, now: datetime | None = None
) -> Evaluation:
    """Flags the batch, opens the alert and the notification in one transaction:
    an alert without a notification would go unseen, and the reverse untraced.

    A batch already non-compliant opens nothing more, or a broken air
    conditioner would notify every five minutes.
    """
    reference_now = _as_naive_utc(now or datetime.now(UTC))
    measured_at = _as_naive_utc(reading.measured_at)

    row = (
        await session.execute(
            select(Batch, Country, Warehouse.warehouse_ref)
            .join(SensorAssignment, SensorAssignment.batch_id == Batch.batch_id)
            .join(Sensor, Sensor.sensor_id == SensorAssignment.sensor_id)
            .join(Warehouse, Warehouse.warehouse_id == Batch.warehouse_id)
            .join(Country, Country.country_id == Warehouse.country_id)
            .where(
                Sensor.code == reading.sensor_code,
                Sensor.is_active,
                SensorAssignment.assigned_at <= measured_at,
                _released_after(measured_at),
            )
        )
    ).first()

    if row is None:
        return Evaluation()

    batch, country, warehouse_ref = row
    # Read before writing: after a rollback, touching the expired instance
    # triggers a sync reload and a MissingGreenlet.
    batch_id = batch.batch_id
    warehouse_id = batch.warehouse_id

    if is_within_band(reading.temperature, reading.humidity, country):
        # Back in band does not restore `is_compliant`: resolving the room's alert does.
        return Evaluation(batch_id=batch_id, within_band=True)

    if not batch.is_compliant:
        return Evaluation(batch_id=batch_id, within_band=False)

    current = Sample(measured_at, reading.temperature, reading.humidity)
    previous = None
    if all(_out_of_band(current, country)):
        # Only case where the triggering reading alone cannot decide.
        previous = await _previous_sample(session, reading.sensor_code, measured_at)
    breached_metric = select_breached_metric(current, previous, country)

    batch.is_compliant = False

    alert_id = uuid.uuid4()
    alert = Alert(
        alert_id=alert_id,
        warehouse_id=warehouse_id,
        # A condition alert concerns the room; the notification carries the batch.
        batch_id=None,
        alert_type="condition",
        alert_status="active",
        created_at=reference_now,
        # Not `reference_now`: a buffered or late reading must still date the drift.
        measured_at=measured_at,
    )
    session.add(alert)

    notification = Notification(
        notification_id=uuid.uuid4(),
        warehouse_id=warehouse_id,
        notification_type="batch_non_compliant",
        batch_id=batch_id,
        order_id=None,
        created_at=reference_now,
    )
    session.add(notification)

    try:
        await session.commit()
    except IntegrityError:
        # Concurrent readings both passed the read above, and the partial unique
        # index allows one active condition alert per room. The room is already
        # flagged; the batch flag and its notification still apply.
        await session.rollback()
        return await _flag_batch_only(session, batch_id, reference_now)

    return Evaluation(
        batch_id=batch_id,
        within_band=False,
        alert_created=True,
        notification_created=True,
        compliance_flipped=True,
        # Only after commit: a rejected alert must never reach head office.
        alert_push=AlertPush(
            source_alert_id=alert_id,
            warehouse_ref=warehouse_ref,
            metric=breached_metric,
            measured_at=measured_at,
        ),
    )


async def _flag_batch_only(
    session: AsyncSession, batch_id: uuid.UUID, moment: datetime
) -> Evaluation:
    batch = await session.get(Batch, batch_id)
    if batch is None or not batch.is_compliant:
        return Evaluation(batch_id=batch_id, within_band=False)

    batch.is_compliant = False
    session.add(
        Notification(
            notification_id=uuid.uuid4(),
            warehouse_id=batch.warehouse_id,
            notification_type="batch_non_compliant",
            batch_id=batch.batch_id,
            order_id=None,
            created_at=moment,
        )
    )
    await session.commit()

    return Evaluation(
        batch_id=batch_id,
        within_band=False,
        alert_created=False,
        notification_created=True,
        compliance_flipped=True,
    )
