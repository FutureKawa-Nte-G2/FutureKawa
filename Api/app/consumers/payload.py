"""Decoding an MQTT message into a reading, kept apart so it tests without a broker.

Contract (US #24):

    topic   : futurekawa/<sensor code>
    payload : {"measuredAt": "2026-09-06T12:00:00Z", "temp": 21.5, "humidity": 54.1}
"""

import json
from datetime import UTC, datetime
from decimal import Decimal, InvalidOperation

from app.services.ingestion import Reading

TOPIC_PREFIX = "futurekawa"
TOPIC_FILTER = f"{TOPIC_PREFIX}/+"


class MalformedPayloadError(Exception):
    """Not a failure: a misbehaving firmware must not stop the consumer."""


def sensor_code_from_topic(topic: str) -> str:
    """Read from the topic, not the body: the broker controls the topic, not the firmware."""
    parts = topic.strip("/").split("/")
    if len(parts) < 2 or parts[0] != TOPIC_PREFIX or not parts[-1]:
        raise MalformedPayloadError(f"Topic outside the contract: {topic!r}")
    return parts[-1]


def decode(topic: str, payload: bytes) -> Reading:
    sensor_code = sensor_code_from_topic(topic)

    try:
        body = json.loads(payload)
    except (json.JSONDecodeError, UnicodeDecodeError) as exc:
        raise MalformedPayloadError(f"Unreadable body: {exc}") from exc

    if not isinstance(body, dict):
        raise MalformedPayloadError("The body must be a JSON object.")

    try:
        # Through str, never float: a float would store 21.4999999999999996.
        temperature = Decimal(str(body["temp"]))
        humidity = Decimal(str(body["humidity"]))
    except (KeyError, InvalidOperation, TypeError) as exc:
        raise MalformedPayloadError(f"Missing or unreadable metric: {exc}") from exc

    measured_at = _parse_instant(body.get("measuredAt"))

    return Reading(
        sensor_code=sensor_code,
        measured_at=measured_at,
        temperature=temperature,
        humidity=humidity,
    )


def _parse_instant(raw: object) -> datetime:
    """Falls back to now: a firmware without a synced clock sends no date,
    and rejecting the reading would lose it for no good reason.
    """
    if raw is None:
        return datetime.now(UTC)
    if not isinstance(raw, str):
        raise MalformedPayloadError("measuredAt must be an ISO 8601 string.")
    try:
        return datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError as exc:
        raise MalformedPayloadError(f"Unreadable measuredAt: {raw!r}") from exc
