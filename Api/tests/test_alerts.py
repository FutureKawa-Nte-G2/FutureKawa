import os

import pytest

from app.security import API_KEY_ENV_VAR, require_api_key
from tests.conftest import (
    ACTIVE_ALERT_IDS_NEWEST_FIRST,
    ALERT_IDS_NEWEST_FIRST,
    API_KEY,
    BATCH_REF,
    CONDITION_ALERT_ID,
    EXPIRATION_ALERT_AT,
    EXPIRATION_ALERT_ID,
    RESOLVED_ALERT_AT,
    RESOLVED_ALERT_ID,
    WAREHOUSE_A_REF,
    WAREHOUSE_B_REF,
)

pytestmark = pytest.mark.asyncio


def _by_id(payload: list[dict], alert_id: int) -> dict:
    return next(alert for alert in payload if alert["id"] == alert_id)


# --- listing ---------------------------------------------------------------


async def test_should_list_the_alerts_of_the_whole_country(client):
    response = await client.get("/api/alerts")

    assert response.status_code == 200
    assert [alert["id"] for alert in response.json()] == ALERT_IDS_NEWEST_FIRST


async def test_should_return_alerts_newest_first(client):
    response = await client.get("/api/alerts")

    dates = [alert["createdAt"] for alert in response.json()]
    assert dates == sorted(dates, reverse=True)


async def test_should_expose_every_field_head_office_stores(client):
    response = await client.get("/api/alerts")

    assert set(response.json()[0]) == {
        "id",
        "type",
        "state",
        "warehouseRef",
        "batchRef",
        "value",
        "createdAt",
        "resolvedAt",
    }


async def test_should_name_fields_in_camel_case_for_the_dotnet_consumer(client):
    """Head office deserialises case-insensitively, which tolerates a different
    case but not a different name: a snake_case key binds to nothing and lands
    as null without raising.
    """
    alert = (await client.get("/api/alerts")).json()[0]

    assert "warehouseRef" in alert
    assert "warehouse_ref" not in alert


# --- warehouse attribution -------------------------------------------------


async def test_should_attribute_a_condition_alert_to_its_own_warehouse(client):
    payload = (await client.get("/api/alerts")).json()

    assert _by_id(payload, CONDITION_ALERT_ID)["warehouseRef"] == WAREHOUSE_A_REF


async def test_should_attribute_an_expiration_alert_through_its_batch(client):
    """An expiration alert carries no warehouse_id: the only path to the
    warehouse goes through the batch it concerns.
    """
    payload = (await client.get("/api/alerts")).json()

    assert _by_id(payload, EXPIRATION_ALERT_ID)["warehouseRef"] == WAREHOUSE_A_REF


async def test_should_attribute_every_alert_to_a_warehouse(client):
    """Head office cannot dispatch an alert it cannot attribute."""
    payload = (await client.get("/api/alerts")).json()

    assert all(alert["warehouseRef"] is not None for alert in payload)


async def test_should_name_the_batch_only_on_an_expiration_alert(client):
    payload = (await client.get("/api/alerts")).json()

    assert _by_id(payload, EXPIRATION_ALERT_ID)["batchRef"] == BATCH_REF
    assert _by_id(payload, CONDITION_ALERT_ID)["batchRef"] is None


async def test_should_use_references_never_internal_ids(client):
    """Head office has its own ids and no way to map ours."""
    payload = (await client.get("/api/alerts")).json()

    assert payload[0]["warehouseRef"] in {WAREHOUSE_A_REF, WAREHOUSE_B_REF}
    assert "warehouseId" not in payload[0]
    assert "batchId" not in payload[0]


# --- incremental pull ------------------------------------------------------


async def test_should_return_only_what_came_after_since(client):
    response = await client.get(
        "/api/alerts", params={"since": RESOLVED_ALERT_AT.isoformat()}
    )

    assert [alert["id"] for alert in response.json()] == [EXPIRATION_ALERT_ID]


async def test_should_exclude_the_alert_sitting_exactly_on_since(client):
    """`since` is exclusive so head office can pass back the createdAt of the
    last alert it stored without receiving it a second time.
    """
    response = await client.get(
        "/api/alerts", params={"since": EXPIRATION_ALERT_AT.isoformat()}
    )

    assert response.json() == []


async def test_should_return_everything_when_since_is_omitted(client):
    response = await client.get("/api/alerts")

    assert len(response.json()) == len(ALERT_IDS_NEWEST_FIRST)


async def test_should_reject_a_malformed_since(client):
    response = await client.get("/api/alerts", params={"since": "not-a-date"})

    assert response.status_code == 422


# --- state filter ----------------------------------------------------------


async def test_should_filter_on_active_state(client):
    response = await client.get("/api/alerts", params={"state": "active"})

    assert [alert["id"] for alert in response.json()] == ACTIVE_ALERT_IDS_NEWEST_FIRST


async def test_should_filter_on_resolved_state(client):
    response = await client.get("/api/alerts", params={"state": "resolved"})

    assert [alert["id"] for alert in response.json()] == [RESOLVED_ALERT_ID]


async def test_should_expose_resolved_at_only_on_a_resolved_alert(client):
    payload = (await client.get("/api/alerts")).json()

    assert _by_id(payload, RESOLVED_ALERT_ID)["resolvedAt"] is not None
    assert _by_id(payload, CONDITION_ALERT_ID)["resolvedAt"] is None


async def test_should_reject_an_unknown_state_rather_than_ignoring_it(client):
    """Returning everything on a typo would read as "no alerts match" and hide
    the mistake.
    """
    response = await client.get("/api/alerts", params={"state": "acive"})

    assert response.status_code == 422


async def test_should_combine_since_and_state(client):
    response = await client.get(
        "/api/alerts",
        params={"since": RESOLVED_ALERT_AT.isoformat(), "state": "active"},
    )

    assert [alert["id"] for alert in response.json()] == [EXPIRATION_ALERT_ID]


# --- authentication --------------------------------------------------------


async def test_should_refuse_a_request_without_an_api_key(client):
    response = await client.get("/api/alerts", headers={"X-API-Key": ""})

    assert response.status_code == 401
    assert response.json()["detail"]["code"] == "invalid_api_key"


async def test_should_refuse_a_wrong_api_key(client):
    response = await client.get("/api/alerts", headers={"X-API-Key": "wrong"})

    assert response.status_code == 401


async def test_should_refuse_to_serve_when_the_key_is_not_configured(monkeypatch):
    """Guards the seam: a route reachable from outside the country network must
    never fall back to open when the secret is missing.
    """
    monkeypatch.delenv(API_KEY_ENV_VAR, raising=False)

    with pytest.raises(RuntimeError):
        await require_api_key(x_api_key=API_KEY)


async def test_should_accept_the_configured_key(api_key):
    os.environ[API_KEY_ENV_VAR] = api_key

    assert await require_api_key(x_api_key=api_key) is None
