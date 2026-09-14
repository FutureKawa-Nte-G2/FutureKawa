"""Pushes condition alerts to head office (`POST /api/alerts`).

The payload follows head office's `CreateAlertRequest`: see
`Documentation/api-pays-contrats.md`.
"""

import asyncio
import logging
import os
import uuid
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from datetime import UTC, datetime
from typing import Literal

import httpx

from app.security import API_KEY_ENV_VAR

logger = logging.getLogger(__name__)

HEAD_OFFICE_ALERTS_URL_ENV_VAR = "HEAD_OFFICE_ALERTS_URL"

# About 30 s in all: enough to ride out a head office restart without pinning
# a task for long.
MAX_ATTEMPTS = 5
BASE_DELAY_SECONDS = 2.0
REQUEST_TIMEOUT_SECONDS = 5.0
# So a long Retry-After does not park the task for an hour.
MAX_RETRY_AFTER_SECONDS = 60.0

# Head office and frontend vocabulary, unlike our room-level `condition`.
Metric = Literal["temperature", "humidity"]

Sleep = Callable[[float], Awaitable[object]]


@dataclass(frozen=True)
class AlertPush:
    source_alert_id: uuid.UUID
    warehouse_ref: str
    metric: Metric
    measured_at: datetime

    def to_payload(self) -> dict[str, str]:
        measured_at = self.measured_at
        # Stored as naive UTC: make it explicit so head office never reads local time.
        if measured_at.tzinfo is None:
            measured_at = measured_at.replace(tzinfo=UTC)
        return {
            "warehouseReference": self.warehouse_ref,
            "type": self.metric,
            "measuredAt": measured_at.astimezone(UTC).isoformat().replace("+00:00", "Z"),
            "sourceAlertId": str(self.source_alert_id),
        }


def _is_retryable(status_code: int) -> bool:
    # A 400, 401 or 404 would get the same answer again and only spend the rate limit.
    return status_code == httpx.codes.TOO_MANY_REQUESTS or status_code >= 500


def _retry_after_seconds(response: httpx.Response) -> float | None:
    value = response.headers.get("Retry-After")
    if value is None:
        return None
    try:
        seconds = float(value)
    except ValueError:
        # HTTP-date form: rare enough to fall back on our own backoff.
        return None
    return min(max(seconds, 0.0), MAX_RETRY_AFTER_SECONDS)


async def push_alert(
    client: httpx.AsyncClient, push: AlertPush, *, sleep: Sleep = asyncio.sleep
) -> bool:
    """Never raises on a head office failure: it runs beside a consumer that must keep going.

    Retries are in memory, so a push pending at shutdown is lost; every
    abandoned push is logged with its alert id so the loss is never silent.
    """
    url = os.environ.get(HEAD_OFFICE_ALERTS_URL_ENV_VAR)
    api_key = os.environ.get(API_KEY_ENV_VAR)
    if not url or not api_key:
        logger.error(
            "Alert %s not pushed to head office: %s or %s is not set.",
            push.source_alert_id,
            HEAD_OFFICE_ALERTS_URL_ENV_VAR,
            API_KEY_ENV_VAR,
        )
        return False

    last_failure = ""
    for attempt in range(1, MAX_ATTEMPTS + 1):
        delay = BASE_DELAY_SECONDS * 2 ** (attempt - 1)
        try:
            response = await client.post(
                url,
                json=push.to_payload(),
                headers={"X-API-Key": api_key},
                timeout=REQUEST_TIMEOUT_SECONDS,
            )
        except httpx.TransportError as exc:
            last_failure = type(exc).__name__
        else:
            if response.is_success:
                logger.info(
                    "Alert %s pushed to head office (%s, %s).",
                    push.source_alert_id,
                    push.warehouse_ref,
                    push.metric,
                )
                return True
            if not _is_retryable(response.status_code):
                logger.error(
                    "Alert %s rejected by head office with HTTP %s (%s); not retrying.",
                    push.source_alert_id,
                    response.status_code,
                    push.warehouse_ref,
                )
                return False
            last_failure = f"HTTP {response.status_code}"
            delay = _retry_after_seconds(response) or delay

        if attempt < MAX_ATTEMPTS:
            logger.warning(
                "Alert %s push attempt %s/%s failed (%s); retrying in %ss.",
                push.source_alert_id,
                attempt,
                MAX_ATTEMPTS,
                last_failure,
                delay,
            )
            await sleep(delay)

    logger.error(
        "Alert %s not delivered to head office after %s attempts (last: %s).",
        push.source_alert_id,
        MAX_ATTEMPTS,
        last_failure,
    )
    return False
