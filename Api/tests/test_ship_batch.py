"""`PATCH /api/batches/{batch_ref}/ship`, called by head office (#92)."""

from datetime import date

import pytest
from sqlalchemy import select

from app.models import Batch
from tests.conftest import BATCH_REF

pytestmark = pytest.mark.asyncio

URL = f"/api/batches/{BATCH_REF}/ship"
SHIPPED_AT = date(2026, 9, 15)


async def _batch(session) -> Batch:
    return await session.scalar(select(Batch).where(Batch.batch_ref == BATCH_REF))


async def test_should_mark_the_batch_as_shipped(session, client, auth_headers):
    response = await client.patch(
        URL, json={"shippedAt": SHIPPED_AT.isoformat()}, headers=auth_headers
    )

    assert response.status_code == 200
    body = response.json()
    assert body["shippedAt"] == SHIPPED_AT.isoformat()
    assert body["batchStatus"] == "shipped"

    batch = await _batch(session)
    assert batch.shipped_at == SHIPPED_AT
    assert batch.batch_status == "shipped"


async def test_should_keep_the_first_date_when_shipment_is_replayed(
    session, client, auth_headers
):
    await client.patch(URL, json={"shippedAt": SHIPPED_AT.isoformat()}, headers=auth_headers)

    response = await client.patch(
        URL, json={"shippedAt": date(2026, 9, 20).isoformat()}, headers=auth_headers
    )

    assert response.status_code == 200
    assert response.json()["shippedAt"] == SHIPPED_AT.isoformat()


async def test_should_answer_404_for_an_unknown_batch(client, auth_headers):
    response = await client.patch(
        "/api/batches/LOT-UNKNOWN/ship",
        json={"shippedAt": SHIPPED_AT.isoformat()},
        headers=auth_headers,
    )

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "batch_not_found"


async def test_should_refuse_without_the_api_key(session, client):
    response = await client.patch(URL, json={"shippedAt": SHIPPED_AT.isoformat()})

    assert response.status_code == 401
    assert (await _batch(session)).shipped_at is None


async def test_should_reject_a_payload_without_a_date(client, auth_headers):
    response = await client.patch(URL, json={}, headers=auth_headers)

    assert response.status_code == 422
