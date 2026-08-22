import pytest
from sqlalchemy import func, select

from app.services.batches import STATUS_ON_CREATION
from tests.conftest import (
    OTHER_FARM_ID,
    OTHER_FARM_REF,
    OTHER_WAREHOUSE_ID,
    OTHER_WAREHOUSE_REF,
    SEEDED_FARM_ID,
    SEEDED_WAREHOUSE_ID,
    STORED_AT,
    Batch,
)

pytestmark = pytest.mark.asyncio


# --- creation --------------------------------------------------------------


async def test_should_create_batch_when_both_references_resolve(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.status_code == 201
    assert response.json()["batch_ref"] == "BR-2026-00042"


async def test_should_return_generated_id_so_the_watcher_needs_no_second_request(
    client, session, valid_payload
):
    response = await client.post("/api/batches", json=valid_payload)

    persisted_id = await session.scalar(select(Batch.id))
    assert response.json()["id"] == persisted_id


async def test_should_return_every_field_of_the_created_batch(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert set(response.json()) == {
        "id",
        "batch_ref",
        "farm_id",
        "warehouse_id",
        "quality_grade",
        "stored_at",
        "shipped_at",
        "status",
    }


# --- ERP reference resolution ---------------------------------------------


async def test_should_resolve_farm_ref_to_our_own_id(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["farm_id"] == SEEDED_FARM_ID


async def test_should_resolve_warehouse_ref_to_our_own_id(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["warehouse_id"] == SEEDED_WAREHOUSE_ID


async def test_should_resolve_each_reference_independently(client, valid_payload):
    response = await client.post(
        "/api/batches",
        json={
            **valid_payload,
            "farm_ref": OTHER_FARM_REF,
            "warehouse_ref": OTHER_WAREHOUSE_REF,
        },
    )

    assert response.json()["farm_id"] == OTHER_FARM_ID
    assert response.json()["warehouse_id"] == OTHER_WAREHOUSE_ID


async def test_should_persist_batch_ref_verbatim(client, session, valid_payload):
    await client.post("/api/batches", json=valid_payload)

    assert await session.scalar(select(Batch.batch_ref)) == "BR-2026-00042"


# --- server-imposed fields -------------------------------------------------


async def test_should_set_status_to_compliant_on_creation(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["status"] == "compliant"
    assert STATUS_ON_CREATION == "compliant"


async def test_should_leave_shipped_at_null_on_creation(client, valid_payload):
    """A batch enters the FIFO the moment it is created: `shipped_at` is what
    takes it out, and only the delivery file may set it.
    """
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["shipped_at"] is None


async def test_should_take_stored_at_from_the_file(client, valid_payload):
    response = await client.post("/api/batches", json=valid_payload)

    assert response.json()["stored_at"] == STORED_AT.isoformat()


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("id", 999),
        ("status", "expired"),
        ("shipped_at", "2026-08-01"),
        ("farm_id", 999),
        ("warehouse_id", 999),
    ],
)
async def test_should_ignore_server_owned_fields_sent_in_the_body(
    client, valid_payload, field, value
):
    response = await client.post("/api/batches", json={**valid_payload, field: value})

    assert response.status_code == 201
    assert response.json()[field] != value


# --- duplicates ------------------------------------------------------------


async def test_should_reject_a_batch_ref_already_in_the_database(
    client, valid_payload
):
    """A reception file replayed by the ERP must not create the batch twice."""
    await client.post("/api/batches", json=valid_payload)

    response = await client.post("/api/batches", json=valid_payload)

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "batch_ref_already_exists"


async def test_should_keep_a_single_row_when_a_batch_ref_is_replayed(
    client, session, valid_payload
):
    await client.post("/api/batches", json=valid_payload)
    await client.post("/api/batches", json=valid_payload)

    assert await session.scalar(select(func.count()).select_from(Batch)) == 1


async def test_should_still_accept_other_batches_after_a_duplicate_was_refused(
    client, valid_payload
):
    """The rollback must leave the session usable: one bad file cannot poison
    the ones the watcher processes next.
    """
    await client.post("/api/batches", json=valid_payload)
    await client.post("/api/batches", json=valid_payload)

    response = await client.post(
        "/api/batches", json={**valid_payload, "batch_ref": "BR-2026-00043"}
    )

    assert response.status_code == 201


# --- unknown references ----------------------------------------------------


async def test_should_reject_when_farm_ref_is_unknown(client, valid_payload):
    response = await client.post(
        "/api/batches", json={**valid_payload, "farm_ref": "BR-EXP-99"}
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "farm_not_found"


async def test_should_reject_when_warehouse_ref_is_unknown(client, valid_payload):
    response = await client.post(
        "/api/batches", json={**valid_payload, "warehouse_ref": "BR-ENT-99"}
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "warehouse_not_found"


@pytest.mark.parametrize(
    ("field", "value"),
    [("farm_ref", "BR-EXP-99"), ("warehouse_ref", "BR-ENT-99")],
)
async def test_should_not_persist_batch_when_a_reference_is_unknown(
    client, session, valid_payload, field, value
):
    await client.post("/api/batches", json={**valid_payload, field: value})

    assert await session.scalar(select(func.count()).select_from(Batch)) == 0


# --- schema validation -----------------------------------------------------


@pytest.mark.parametrize(
    "missing_field", ["batch_ref", "farm_ref", "warehouse_ref", "stored_at"]
)
async def test_should_reject_when_a_required_field_is_missing(
    client, valid_payload, missing_field
):
    payload = {k: v for k, v in valid_payload.items() if k != missing_field}

    response = await client.post("/api/batches", json=payload)

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("stored_at", "not-a-date"),
        ("batch_ref", ""),
        ("farm_ref", ""),
        ("warehouse_ref", ""),
    ],
)
async def test_should_reject_a_malformed_field(client, valid_payload, field, value):
    response = await client.post("/api/batches", json={**valid_payload, field: value})

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


async def test_should_accept_a_batch_without_quality_grade(client, valid_payload):
    """The corrected model leaves `quality_grade` nullable: the ERP does not
    always carry it, and that is not a reason to refuse the batch.
    """
    payload = {k: v for k, v in valid_payload.items() if k != "quality_grade"}

    response = await client.post("/api/batches", json=payload)

    assert response.status_code == 201
    assert response.json()["quality_grade"] is None
