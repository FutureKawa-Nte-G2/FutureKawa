"""Consumer 1 — écriture des relevés.

Un capteur appartient à une salle, pas à un lot. Il publie en continu, y compris
quand la salle est vide ou que le lot qu'il suivait est parti. Écrire tout ce
qui arrive remplirait `measurements` de bruit qu'aucune requête ne sait plus
rattacher à quoi que ce soit.

Le filtre est donc l'assignation : un relevé n'est conservé que si le capteur
suivait un lot au moment où il a mesuré. C'est le critère que l'US #30 énonce —
`SENSOR_ASSIGNMENT.released_at IS NULL` — étendu ici à la date du relevé plutôt
qu'à l'instant présent, pour qu'un message retardé par le broker soit jugé sur
le moment où il a été pris.

Ce module ne sait rien de MQTT. Il reçoit un relevé déjà décodé, ce qui le rend
testable sans broker — le câblage vit dans `app/consumers/`.
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
    """Un relevé, tel qu'un capteur le publie.

    Le capteur est désigné par son `code` — le topic MQTT sur lequel il
    publie — jamais par un identifiant interne : le firmware ne connaît pas nos
    UUID, et rien ne les lui apprend.
    """

    sensor_code: str
    measured_at: datetime
    temperature: Decimal
    humidity: Decimal


async def persist_reading(session: AsyncSession, reading: Reading) -> uuid.UUID | None:
    """Écrit le relevé s'il concerne un lot, sinon l'ignore.

    Renvoie l'identifiant écrit, ou `None` quand le relevé a été reçu et
    volontairement laissé de côté — un capteur inconnu, inactif, ou au repos
    entre deux lots. Aucun de ces cas n'est une erreur : ils sont le
    fonctionnement normal d'une salle où les capteurs tournent en permanence.

    Idempotent. Un broker MQTT en QoS 1 redélivre, et rejouer un message ne doit
    pas dupliquer une ligne — d'où la contrainte `(sensor_id, meas_date)` en
    base et le rattrapage de son violation ici.
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
        # Déjà écrit : le broker a redélivré. Rien à faire, et surtout pas
        # remonter une erreur qui ferait boucler la redélivraison.
        await session.rollback()
        return None

    return measurement.measurement_id


def _released_after(moment: datetime):
    """Assignation encore ouverte, ou fermée après le relevé."""
    return (SensorAssignment.released_at.is_(None)) | (
        SensorAssignment.released_at > moment
    )


def _as_naive_utc(value: datetime) -> datetime:
    """Ramène en UTC naïf.

    SQLite rend un `timestamptz` naïf là où PostgreSQL le rend conscient :
    comparer les deux lèverait un TypeError selon le moteur. Tout est comparé
    en UTC ici, donc le décalage ne porte plus d'information une fois normalisé.
    """
    if value.tzinfo is None:
        return value
    return value.astimezone(UTC).replace(tzinfo=None)
