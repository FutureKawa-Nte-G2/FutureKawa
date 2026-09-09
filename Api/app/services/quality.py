"""Consumer 2 — évaluation des seuils.

Indépendant du consumer d'écriture : les deux sont abonnés au même topic et un
échec de l'un ne doit pas priver l'autre de son message. Un relevé peut donc
déclencher une alerte sans avoir été conservé, et inversement — c'est voulu.

La chaîne de résolution est celle de l'US #30 : capteur → assignation active →
lot → entrepôt → pays, qui porte les seuils.
"""

import uuid
from dataclasses import dataclass
from datetime import UTC, datetime
from decimal import Decimal

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.models import (
    Alert,
    Batch,
    Country,
    Notification,
    Sensor,
    SensorAssignment,
    Warehouse,
)
from app.services.ingestion import Reading, _as_naive_utc, _released_after


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


@dataclass(frozen=True)
class Evaluation:
    """Ce que l'évaluation d'un relevé a produit."""

    batch_id: uuid.UUID | None = None
    within_band: bool = True
    alert_created: bool = False
    notification_created: bool = False
    # Vrai la première fois que le lot bascule, faux les fois suivantes.
    compliance_flipped: bool = False


def is_within_band(
    temperature: Decimal, humidity: Decimal, country: Country
) -> bool:
    """Le relevé tient-il dans les deux bandes du pays.

    Fonction pure, sans base : c'est la règle métier de l'US #30, et elle se
    teste sans monter quoi que ce soit.
    """
    temp_band = Band(country.nominal_temp, country.tolerance_temp)
    humidity_band = Band(country.nominal_humidity, country.tolerance_humidity)
    return temp_band.contains(temperature) and humidity_band.contains(humidity)


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
            select(Batch, Country)
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

    batch, country = row
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

    batch.is_compliant = False

    alert = Alert(
        alert_id=uuid.uuid4(),
        warehouse_id=warehouse_id,
        # `condition` concerne la salle, pas le lot : le modèle réserve
        # `batch_id` aux alertes d'expiration. Le lien vers le lot est porté par
        # la notification, qui est ce que le frontend ouvre au clic.
        batch_id=None,
        alert_type="condition",
        alert_status="active",
        created_at=reference_now,
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
