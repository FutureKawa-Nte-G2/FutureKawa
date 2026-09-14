"""Pushing condition alerts to head office.

`quality.py` opens an alert in the country database; head office used to learn
about it only if it came asking. It now receives each alert as soon as it is
committed, on `POST /api/alerts`
(`backend/FutureKawaSiege.API/Controllers/AlertsController.cs`).

The contract is head office's, not ours — `CreateAlertRequest`:

  * `warehouseReference`: our `warehouses.warehouse_ref`, never an internal id;
  * `type`: `temperature` or `humidity`, the vocabulary head office and the
    frontend display, not our room-level `condition`;
  * `measuredAt`: when the triggering reading was taken, not when the alert was
    written;
  * `sourceAlertId`: our `alert_id`, which head office keeps to push the
    resolution back to `PATCH /api/alerts/{id}/resolve`.

Authentication is the country key head office holds under
`LocalApi:Countries:{code}:ApiKey` — the same secret it sends us on the way
back, so this API reads it from `LOCAL_API_KEY` rather than a second variable.

Retries live in memory. A push still pending when the process stops is lost,
but never silently: every abandoned push ends on an error log naming the alert.
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

MAX_ATTEMPTS = 5
# Doubled after each failure: 2, 4, 8 then 16 seconds, about half a minute in
# all — enough to ride out a head office restart without pinning a task for long.
BASE_DELAY_SECONDS = 2.0
REQUEST_TIMEOUT_SECONDS = 5.0
# A `Retry-After` is honoured up to this: an hour-long answer would otherwise
# park the task for an hour.
MAX_RETRY_AFTER_SECONDS = 60.0

Metric = Literal["temperature", "humidity"]

Sleep = Callable[[float], Awaitable[object]]


@dataclass(frozen=True)
class AlertPush:
    """One alert, as head office is to receive it."""

    source_alert_id: uuid.UUID
    warehouse_ref: str
    metric: Metric
    measured_at: datetime

    def to_payload(self) -> dict[str, str]:
        # Stored as naive UTC (see `_as_naive_utc`): stated explicitly on the
        # wire, so head office never reads it as its own local time.
        measured_at = self.measured_at
        if measured_at.tzinfo is None:
            measured_at = measured_at.replace(tzinfo=UTC)
        return {
            "warehouseReference": self.warehouse_ref,
            "type": self.metric,
            "measuredAt": measured_at.astimezone(UTC).isoformat().replace("+00:00", "Z"),
            "sourceAlertId": str(self.source_alert_id),
        }


def _is_retryable(status_code: int) -> bool:
    """Worth another try: head office overloaded or down, not refusing us.

    A `400`, `401` or `404` will answer the same thing next time — a malformed
    payload, a key it does not know, a warehouse it does not have. Retrying
    those only spends its rate limit.
    """
    return status_code == httpx.codes.TOO_MANY_REQUESTS or status_code >= 500


def _retry_after_seconds(response: httpx.Response) -> float | None:
    """The delay a `429` asks for, when given in seconds."""
    value = response.headers.get("Retry-After")
    if value is None:
        return None
    try:
        seconds = float(value)
    except ValueError:
        # The HTTP-date form: rare enough to fall back on our own backoff.
        return None
    return min(max(seconds, 0.0), MAX_RETRY_AFTER_SECONDS)


async def push_alert(
    client: httpx.AsyncClient, push: AlertPush, *, sleep: Sleep = asyncio.sleep
) -> bool:
    """Send one alert to head office, retrying while it is worth it.

    Returns whether head office accepted it. Never raises on a head office
    failure: the caller runs this beside a consumer that must keep going.

    The API key goes in a header and is never logged — neither here nor by
    httpx, whose request log carries the method, URL and status only.
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
