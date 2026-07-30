from datetime import UTC, datetime

import pytest
from sqlalchemy import func, select

from app.schemas.batch import BatchCreate, QualityGrade
from app.security import get_current_user
from app.services.batches import STATUS_ON_CREATION, create_batch
from tests.conftest import CALLER, OTHER_FARM_ID, SEEDED_FARM_ID, Batch

pytestmark = pytest.mark.asyncio


# --- creation --------------------------------------------------------------


async def test_should_create_batch_when_farm_exists(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.status_code == 201
    assert response.json()["farm_id"] == SEEDED_FARM_ID
    assert response.json()["quality"] == "grade_1_specialty"


async def test_should_return_generated_id_so_frontend_needs_no_second_request(
    client, session, valid_payload
):
    response = await client.post("/api/batches", json=valid_payload)

    persisted_id = await session.scalar(select(Batch.id))
    assert response.json()["id"] == persisted_id


async def test_should_return_every_field_of_the_created_batch(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert set(response.json()) == {
        "id",
        "farm_id",
        "warehouse_id",
        "user_id",
        "quality",
        "entered_at",
        "status",
        "is_compliant",
    }


# --- server-imposed fields -------------------------------------------------


async def test_should_take_warehouse_id_from_the_caller_token(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["warehouse_id"] == CALLER.warehouse_id


async def test_should_take_user_id_from_the_caller_token(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["user_id"] == CALLER.id


async def test_should_set_status_to_received_on_creation(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["status"] == "received"
    assert STATUS_ON_CREATION == "received"


async def test_should_set_is_compliant_to_true_on_creation(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["is_compliant"] is True


async def test_should_stamp_entered_at_at_insertion_time(session, valid_payload):
    inserted_at = datetime(2026, 7, 30, 10, 15, 0, tzinfo=UTC)

    batch = await create_batch(
        session,
        BatchCreate(**valid_payload),
        CALLER,
        now=inserted_at,
    )

    # SQLite drops tzinfo on read back; timestamptz keeps it in the real schema.
    assert batch.entered_at.replace(tzinfo=UTC) == inserted_at


async def test_should_default_entered_at_to_now_when_not_injected(
    session, valid_payload
):
    before = datetime.now(UTC).replace(tzinfo=None)

    batch = await create_batch(session, BatchCreate(**valid_payload), CALLER)

    assert batch.entered_at.replace(tzinfo=None) >= before


# --- body cannot override server-imposed fields ---------------------------


async def test_should_ignore_warehouse_id_sent_in_the_body(client, valid_payload):
    response = await client.post(
        "/api/batches", json={**valid_payload, "warehouse_id": 999}
    )

    assert response.status_code == 201
    assert response.json()["warehouse_id"] == CALLER.warehouse_id


async def test_should_ignore_user_id_sent_in_the_body(client, valid_payload):
    response = await client.post("/api/batches", json={**valid_payload, "user_id": 999})

    assert response.status_code == 201
    assert response.json()["user_id"] == CALLER.id


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("id", 999),
        ("entered_at", "2000-01-01T00:00:00Z"),
        ("status", "shipped"),
        ("is_compliant", False),
    ],
)
async def test_should_ignore_other_server_owned_fields_sent_in_the_body(
    client, valid_payload, field, value
):
    response = await client.post("/api/batches", json={**valid_payload, field: value})

    assert response.status_code == 201
    assert response.json()[field] != value


# --- validation ------------------------------------------------------------


async def test_should_reject_when_farm_id_does_not_exist(client, valid_payload):
    response = await client.post("/api/batches", json={**valid_payload, "farm_id": 4242})

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "farm_not_found"


async def test_should_not_persist_batch_when_farm_id_does_not_exist(
    client, session, valid_payload
):
    await client.post("/api/batches", json={**valid_payload, "farm_id": 4242})

    assert await session.scalar(select(func.count()).select_from(Batch)) == 0


async def test_should_reject_when_quality_is_outside_the_enum(client, valid_payload):
    response = await client.post(
        "/api/batches", json={**valid_payload, "quality": "grade_9_imaginary"}
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


@pytest.mark.parametrize("quality", [grade.value for grade in QualityGrade])
async def test_should_accept_every_value_of_the_quality_enum(
    client, valid_payload, quality
):
    response = await client.post("/api/batches", json={**valid_payload, "quality": quality})

    assert response.status_code == 201


@pytest.mark.parametrize("missing_field", ["farm_id", "quality"])
async def test_should_reject_when_a_required_field_is_missing(
    client, valid_payload, missing_field
):
    payload = {k: v for k, v in valid_payload.items() if k != missing_field}

    response = await client.post("/api/batches", json=payload)

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


async def test_should_reject_when_farm_id_is_not_an_integer(client, valid_payload):
    response = await client.post(
        "/api/batches", json={**valid_payload, "farm_id": "not-an-id"}
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


async def test_should_create_batch_for_any_existing_farm(client, valid_payload):
    response = await client.post(
        "/api/batches", json={**valid_payload, "farm_id": OTHER_FARM_ID}
    )

    assert response.status_code == 201
    assert response.json()["farm_id"] == OTHER_FARM_ID


# --- auth seam -------------------------------------------------------------


async def test_should_refuse_to_serve_until_jwt_decoding_is_wired():
    """Guards the seam: the endpoint must not fall back to a placeholder user,
    which would file batches into an arbitrary warehouse.
    """
    with pytest.raises(NotImplementedError):
        await get_current_user()
