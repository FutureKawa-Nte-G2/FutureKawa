"""Décodage d'un message MQTT en relevé.

Isolé du transport et de la base : c'est la seule partie du chemin MQTT qui a
une vraie logique, et elle se teste sur des chaînes d'octets sans broker ni
connexion.

Le firmware publie du JSON sur un topic qui porte le code du capteur. Le
contrat, tiré de l'US #24 :

    topic   : futurekawa/<code capteur>
    payload : {"measuredAt": "2026-09-06T12:00:00Z", "temp": 21.5, "humidity": 54.1}
"""

import json
from datetime import UTC, datetime
from decimal import Decimal, InvalidOperation

from app.services.ingestion import Reading

TOPIC_PREFIX = "futurekawa"
TOPIC_FILTER = f"{TOPIC_PREFIX}/+"


class MalformedPayloadError(Exception):
    """Le message n'est pas un relevé exploitable.

    Distinct d'une panne : un firmware qui publie n'importe quoi ne doit pas
    arrêter le consumer, seulement voir son message écarté et journalisé.
    """


def sensor_code_from_topic(topic: str) -> str:
    """Le code du capteur est le dernier segment du topic.

    Le code fait autorité côté topic plutôt que dans le corps du message : un
    firmware ne peut pas usurper le topic sur lequel le broker l'a autorisé à
    publier, alors qu'il écrit son corps librement.
    """
    parts = topic.strip("/").split("/")
    if len(parts) < 2 or parts[0] != TOPIC_PREFIX or not parts[-1]:
        raise MalformedPayloadError(f"Topic hors contrat : {topic!r}")
    return parts[-1]


def decode(topic: str, payload: bytes) -> Reading:
    """Transforme un message en `Reading`, ou lève `MalformedPayloadError`."""
    sensor_code = sensor_code_from_topic(topic)

    try:
        body = json.loads(payload)
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        raise MalformedPayloadError(f"Corps illisible : {exc}") from exc

    if not isinstance(body, dict):
        raise MalformedPayloadError("Le corps attendu est un objet JSON.")

    try:
        # Decimal depuis la chaîne d'origine, jamais depuis un float : le
        # firmware envoie 21.5, et passer par un float donnerait
        # 21.4999999999999996 dans une colonne numeric(5,2).
        temperature = Decimal(str(body["temp"]))
        humidity = Decimal(str(body["humidity"]))
    except (KeyError, InvalidOperation, TypeError) as exc:
        raise MalformedPayloadError(f"Grandeur absente ou illisible : {exc}") from exc

    measured_at = _parse_instant(body.get("measuredAt"))

    return Reading(
        sensor_code=sensor_code,
        measured_at=measured_at,
        temperature=temperature,
        humidity=humidity,
    )


def _parse_instant(raw: object) -> datetime:
    """Date du relevé, ou l'instant présent à défaut.

    Un firmware sans horloge synchronisée n'envoie pas de date : la réception
    est alors la meilleure approximation disponible, et refuser le message
    ferait perdre la mesure pour une raison qui n'en est pas une.
    """
    if raw is None:
        return datetime.now(UTC)
    if not isinstance(raw, str):
        raise MalformedPayloadError("measuredAt doit être une chaîne ISO 8601.")
    try:
        return datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError as exc:
        raise MalformedPayloadError(f"measuredAt illisible : {raw!r}") from exc
