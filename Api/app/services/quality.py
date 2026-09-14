"""Consumer 2 — évaluation des seuils.

Indépendant du consumer d'écriture : les deux sont abonnés au même topic et un
échec de l'un ne doit pas priver l'autre de son message. Un relevé peut donc
déclencher une alerte sans avoir été conservé, et inversement — c'est voulu.

La chaîne de résolution est celle de l'US #30 : capteur → assignation active →
lot → entrepôt → pays, qui porte les seuils.
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
    """La plage tolérée pour une grandeur.

    Une bande, pas un plafond : un relevé sort de la plage aussi bien par le
    bas que par le haut. Un entrepôt trop froid abîme le café autant qu'un
    entrepôt trop chaud.
    """

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
    """Ce que l'évaluation d'un relevé a produit."""

    batch_id: uuid.UUID | None = None
    within_band: bool = True
    alert_created: bool = False
    notification_created: bool = False
    # Vrai la première fois que le lot bascule, faux les fois suivantes.
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
    """Le relevé tient-il dans les deux bandes du pays.

    Fonction pure, sans base : c'est la règle métier de l'US #30, et elle se
    teste sans monter quoi que ce soit.
    """
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
    """Compare un relevé aux seuils de son pays et réagit s'il en sort.

    Quand un lot conforme reçoit un relevé hors bande, trois écritures ont lieu
    dans la même transaction : `is_compliant` passe à faux, une `ALERT`
    `condition` est ouverte, et une `NOTIFICATION` `batch_non_compliant` est
    créée. Les trois ensemble ou aucune — une alerte sans notification serait
    invisible, une notification sans alerte serait sans trace.

    Anti-spam : un lot déjà non conforme ne rouvre pas d'alerte aux relevés
    suivants. Un entrepôt en panne de climatisation publie un relevé toutes les
    cinq minutes ; sans cette garde, la cloche recevrait 288 notifications par
    jour pour un seul incident.
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
        # Capteur inconnu, inactif, ou au repos : rien à évaluer.
        return Evaluation()

    batch, country, warehouse_ref = row
    # Lu avant toute écriture : un `rollback` expire l'instance, et relire
    # `batch.batch_id` après coup déclencherait un rechargement synchrone —
    # interdit hors greenlet, donc une MissingGreenlet en pleine reprise
    # d'erreur. La reprise doit tenir sans retoucher à l'objet expiré.
    batch_id = batch.batch_id
    warehouse_id = batch.warehouse_id

    if is_within_band(reading.temperature, reading.humidity, country):
        # Le retour dans la bande ne rétablit pas `is_compliant` : un lot qui a
        # passé une nuit hors plage reste suspect tant qu'un humain n'a pas
        # tranché. La levée est une décision, pas une conséquence mécanique.
        return Evaluation(batch_id=batch_id, within_band=True)

    if not batch.is_compliant:
        # Déjà signalé. On ne rouvre rien.
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
        # `condition` concerne la salle, pas le lot : le modèle réserve
        # `batch_id` aux alertes d'expiration. Le lien vers le lot est porté par
        # la notification, qui est ce que le frontend ouvre au clic.
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
        # Une alerte `condition` active existe déjà pour cette salle — l'index
        # unique partiel de `alerts` l'impose, et deux relevés évalués en même
        # temps passent tous deux la lecture qui précède. La salle est déjà
        # signalée ; on garde la bascule du lot et sa notification, qui eux sont
        # propres à ce lot.
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
    """Bascule le lot et le notifie, sans rouvrir d'alerte de salle."""
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
