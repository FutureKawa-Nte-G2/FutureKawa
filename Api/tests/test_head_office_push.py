"""Pushing alerts to head office (#80)."""

import json
import logging
import uuid
from collections.abc import Callable
from datetime import datetime, timedelta, timezone

import httpx
import pytest

from app.services.head_office import (
    BASE_DELAY_SECONDS,
    HEAD_OFFICE_ALERTS_URL_ENV_VAR,
    MAX_ATTEMPTS,
    AlertPush,
    push_alert,
)
from tests.conftest import API_KEY, WAREHOUSE_REF

pytestmark = pytest.mark.asyncio

HEAD_OFFICE_URL = "http://siege.test/api/alerts"
ALERT_ID = uuid.UUID("00000000-0000-0000-0000-00000000a1e7")
MEASURED_AT = datetime(2026, 8, 10, 12, 0, 0)


@pytest.fixture(autouse=True)
def head_office_url(monkeypatch: pytest.MonkeyPatch) -> str:
    monkeypatch.setenv(HEAD_OFFICE_ALERTS_URL_ENV_VAR, HEAD_OFFICE_URL)
    return HEAD_OFFICE_URL


@pytest.fixture
def push() -> AlertPush:
    return AlertPush(
        source_alert_id=ALERT_ID,
        warehouse_ref=WAREHOUSE_REF,
        metric="humidity",
        measured_at=MEASURED_AT,
    )


class FakeSleep:
    def __init__(self) -> None:
        self.delays: list[float] = []

    async def __call__(self, delay: float) -> None:
        self.delays.append(delay)


class HeadOffice:
    def __init__(self, *responses: httpx.Response | Exception) -> None:
        self._responses = list(responses)
        self.requests: list[httpx.Request] = []

    def __call__(self, request: httpx.Request) -> httpx.Response:
        self.requests.append(request)
        response = self._responses.pop(0) if len(self._responses) > 1 else self._responses[0]
        if isinstance(response, Exception):
            raise response
        return response


async def _push_with(
    head_office: Callable[[httpx.Request], httpx.Response],
    push: AlertPush,
    sleep: FakeSleep,
) -> bool:
    async with httpx.AsyncClient(transport=httpx.MockTransport(head_office)) as client:
        return await push_alert(client, push, sleep=sleep)


# --- contract --------------------------------------------------------------


async def test_should_send_the_head_office_contract(push):
    head_office = HeadOffice(httpx.Response(200))

    delivered = await _push_with(head_office, push, FakeSleep())

    assert delivered is True
    [request] = head_office.requests
    assert str(request.url) == HEAD_OFFICE_URL
    assert request.method == "POST"
    assert request.headers["X-API-Key"] == API_KEY
    assert json.loads(request.content) == {
        "warehouseReference": WAREHOUSE_REF,
        "type": "humidity",
        "measuredAt": "2026-08-10T12:00:00Z",
        "sourceAlertId": str(ALERT_ID),
    }


async def test_should_convert_an_aware_timestamp_to_utc():
    brasilia = timezone(timedelta(hours=-3))
    push = AlertPush(
        source_alert_id=ALERT_ID,
        warehouse_ref=WAREHOUSE_REF,
        metric="temperature",
        measured_at=datetime(2026, 8, 10, 9, 0, 0, tzinfo=brasilia),
    )

    assert push.to_payload()["measuredAt"] == "2026-08-10T12:00:00Z"


# --- retries ---------------------------------------------------------------


async def test_should_retry_a_server_error(push):
    head_office = HeadOffice(httpx.Response(503), httpx.Response(200))
    sleep = FakeSleep()

    delivered = await _push_with(head_office, push, sleep)

    assert delivered is True
    assert len(head_office.requests) == 2
    assert sleep.delays == [BASE_DELAY_SECONDS]


async def test_should_retry_a_transport_error(push):
    head_office = HeadOffice(httpx.ConnectError("refused"), httpx.Response(200))

    delivered = await _push_with(head_office, push, FakeSleep())

    assert delivered is True
    assert len(head_office.requests) == 2


async def test_should_honour_retry_after_on_a_rate_limit(push):
    head_office = HeadOffice(
        httpx.Response(429, headers={"Retry-After": "7"}), httpx.Response(200)
    )
    sleep = FakeSleep()

    delivered = await _push_with(head_office, push, sleep)

    assert delivered is True
    assert sleep.delays == [7.0]


async def test_should_cap_an_excessive_retry_after(push):
    head_office = HeadOffice(
        httpx.Response(429, headers={"Retry-After": "3600"}), httpx.Response(200)
    )
    sleep = FakeSleep()

    await _push_with(head_office, push, sleep)

    assert sleep.delays == [60.0]


async def test_should_give_up_after_the_last_attempt_and_say_so(push, caplog):
    head_office = HeadOffice(httpx.Response(500))
    sleep = FakeSleep()

    with caplog.at_level(logging.ERROR, logger="app.services.head_office"):
        delivered = await _push_with(head_office, push, sleep)

    assert delivered is False
    assert len(head_office.requests) == MAX_ATTEMPTS
    assert sleep.delays == [2.0, 4.0, 8.0, 16.0]
    assert str(ALERT_ID) in caplog.text
    assert "not delivered" in caplog.text


# --- refusals --------------------------------------------------------------


@pytest.mark.parametrize("status_code", [400, 401, 404])
async def test_should_not_retry_a_refusal(push, caplog, status_code):
    head_office = HeadOffice(httpx.Response(status_code))
    sleep = FakeSleep()

    with caplog.at_level(logging.ERROR, logger="app.services.head_office"):
        delivered = await _push_with(head_office, push, sleep)

    assert delivered is False
    assert len(head_office.requests) == 1
    assert sleep.delays == []
    assert str(ALERT_ID) in caplog.text


# --- configuration ---------------------------------------------------------


async def test_should_not_push_without_a_head_office_url(push, monkeypatch, caplog):
    monkeypatch.delenv(HEAD_OFFICE_ALERTS_URL_ENV_VAR)
    head_office = HeadOffice(httpx.Response(200))

    with caplog.at_level(logging.ERROR, logger="app.services.head_office"):
        delivered = await _push_with(head_office, push, FakeSleep())

    assert delivered is False
    assert head_office.requests == []
    assert HEAD_OFFICE_ALERTS_URL_ENV_VAR in caplog.text


async def test_should_never_log_the_api_key(push, caplog):
    head_office = HeadOffice(
        httpx.ConnectError("refused"), httpx.Response(503), httpx.Response(401)
    )

    with caplog.at_level(logging.DEBUG):
        await _push_with(head_office, push, FakeSleep())

    assert caplog.records, "the scenario must log something for this check to mean anything"
    assert API_KEY not in caplog.text
