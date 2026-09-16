"""`POST /api/batches` — a batch registered from an ERP reception file.

The caller is a file watcher. Every assertion here is written from what that
watcher has to be able to do: create a batch, tell a replayed file from a wrong
one, and never be left guessing which field the ERP got wrong.
"""

import uuid

import pytest
from sqlalchemy import func, select

from app.models import Batch
from app.security import API_KEY_ENV_VAR
from tests.conftest import (
    API_KEY,
    BATCH_REF,
    FARM_ID,
    FARM_REF,
    NEW_BATCH_REF,
    NEW_STORED_AT,
    WAREHOUSE_ID,
    WAREHOUSE_REF,
)

pytestmark = pytest.mark.asyncio

URL = "/api/batches"


async def _stored_batch(session, batch_ref: str) -> Batch | None:
    return await session.scalar(select(Batch).where(Batch.batch_ref == batch_ref))


# --- nominal creation ------------------------------------------------------


async def test_should_create_batch_when_both_references_resolve(
    client, auth_headers, valid_payload
):
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.status_code == 201


async def test_should_return_a_generated_id_so_the_watcher_needs_no_second_request(
    client, auth_headers, valid_payload
):
    """The id is ours, not the file's: the watcher can log what it created
    without querying us back.
    """
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert uuid.UUID(response.json()["batchId"])


async def test_should_answer_in_camel_case(client, auth_headers, valid_payload):
    """The contract is camelCase, like `/api/measurements` and like the
    frontend's own `Batch` type.
    """
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert set(response.json()) == {
        "batchId",
        "batchRef",
        "farmId",
        "warehouseId",
        "storedAt",
        "shippedAt",
        "qualityGrade",
        "batchStatus",
    }


async def test_should_also_accept_snake_case_from_a_caller_written_on_our_columns(
    client, auth_headers
):
    """`populate_by_name` keeps the column spelling working. camelCase is the
    documented contract, not a trap for whoever reads `models.py` first.
    """
    response = await client.post(
        URL,
        json={
            "batch_ref": NEW_BATCH_REF,
            "farm_ref": FARM_REF,
            "warehouse_ref": WAREHOUSE_REF,
            "stored_at": NEW_STORED_AT.isoformat(),
        },
        headers=auth_headers,
    )

    assert response.status_code == 201


async def test_should_resolve_farm_ref_to_our_own_id(
    client, auth_headers, valid_payload
):
    """The ERP names a farm by its reference and never learns our uuid."""
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.json()["farmId"] == str(FARM_ID)


async def test_should_resolve_warehouse_ref_to_our_own_id(
    client, auth_headers, valid_payload
):
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.json()["warehouseId"] == str(WAREHOUSE_ID)


async def test_should_persist_batch_ref_verbatim(
    client, session, auth_headers, valid_payload
):
    """The reference is the only key shared with the ERP: reformatting it would
    break every later reconciliation.
    """
    await client.post(URL, json=valid_payload, headers=auth_headers)

    assert await _stored_batch(session, NEW_BATCH_REF) is not None


async def test_should_enter_storage_on_creation(client, auth_headers, valid_payload):
    """A batch arriving from a reception file is stored, not received: `stored`
    is the vocabulary of the country database, and head office writes the same
    thing on its side.
    """
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.json()["batchStatus"] == "stored"


async def test_should_leave_shipped_at_null_on_creation(
    client, auth_headers, valid_payload
):
    """A batch enters the FIFO the moment it is created: `shipped_at` is what
    marks it as gone, and the FIFO listing reads exactly this column.
    """
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.json()["shippedAt"] is None


async def test_should_take_stored_at_from_the_file(
    client, auth_headers, valid_payload
):
    """The storage date belongs to the file, not to the moment we processed it:
    a file replayed a day late must not move the batch in the FIFO order.
    """
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.json()["storedAt"] == NEW_STORED_AT.isoformat()


async def test_should_ignore_server_owned_fields_sent_in_the_body(
    client, auth_headers, valid_payload
):
    """A file carrying these is honoured for everything else and silently loses
    them, rather than being allowed to choose its own id or status.
    """
    forged_id = "11111111-1111-1111-1111-111111111111"
    response = await client.post(
        URL,
        json={
            **valid_payload,
            "batchId": forged_id,
            "batchStatus": "shipped",
            "shippedAt": "2026-08-20",
        },
        headers=auth_headers,
    )

    body = response.json()
    assert body["batchId"] != forged_id
    assert body["batchStatus"] == "stored"
    assert body["shippedAt"] is None


# --- a replayed reception file ---------------------------------------------


async def test_should_reject_a_batch_ref_already_in_the_database(
    client, auth_headers, valid_payload
):
    """What stops a reception file replayed by the ERP from creating the batch
    twice.
    """
    response = await client.post(
        URL, json={**valid_payload, "batchRef": BATCH_REF}, headers=auth_headers
    )

    assert response.status_code == 409
    assert response.json()["detail"]["code"] == "batch_already_exists"


async def test_should_keep_a_single_row_when_a_batch_ref_is_replayed(
    client, session, auth_headers, valid_payload
):
    await client.post(URL, json=valid_payload, headers=auth_headers)
    await client.post(URL, json=valid_payload, headers=auth_headers)

    count = await session.scalar(
        select(func.count()).select_from(Batch).where(Batch.batch_ref == NEW_BATCH_REF)
    )
    assert count == 1


async def test_should_still_accept_another_batch_after_a_duplicate_was_refused(
    client, auth_headers, valid_payload
):
    """The rollback must leave the session usable: one bad file cannot poison
    the files queued behind it.
    """
    await client.post(
        URL, json={**valid_payload, "batchRef": BATCH_REF}, headers=auth_headers
    )

    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.status_code == 201


# --- references the country database does not know -------------------------


async def test_should_reject_when_farm_ref_is_unknown(
    client, auth_headers, valid_payload
):
    response = await client.post(
        URL, json={**valid_payload, "farmRef": "BR-EXP-99"}, headers=auth_headers
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "farm_ref_unknown"


async def test_should_reject_when_warehouse_ref_is_unknown(
    client, auth_headers, valid_payload
):
    """Named separately from the farm so the watcher knows which of the two
    references the ERP got wrong.
    """
    response = await client.post(
        URL, json={**valid_payload, "warehouseRef": "BR-ENT-99"}, headers=auth_headers
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "warehouse_ref_unknown"


async def test_should_not_persist_batch_when_a_reference_is_unknown(
    client, session, auth_headers, valid_payload
):
    """Both references are resolved before anything is written."""
    await client.post(
        URL, json={**valid_payload, "farmRef": "BR-EXP-99"}, headers=auth_headers
    )

    assert await _stored_batch(session, NEW_BATCH_REF) is None


# --- malformed files -------------------------------------------------------


@pytest.mark.parametrize(
    "field", ["batchRef", "farmRef", "warehouseRef", "storedAt"]
)
async def test_should_reject_when_a_required_field_is_missing(
    client, auth_headers, valid_payload, field
):
    payload = {k: v for k, v in valid_payload.items() if k != field}

    response = await client.post(URL, json=payload, headers=auth_headers)

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("storedAt", "not-a-date"),
        ("batchRef", ""),
        ("farmRef", ""),
        # varchar(64) in the schema: refused here rather than surfacing as a
        # database error the watcher cannot act on.
        ("batchRef", "X" * 65),
    ],
)
async def test_should_reject_a_malformed_field(
    client, auth_headers, valid_payload, field, value
):
    response = await client.post(
        URL, json={**valid_payload, field: value}, headers=auth_headers
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


# --- quality grade ---------------------------------------------------------


async def test_should_accept_a_batch_without_quality_grade(
    client, auth_headers, valid_payload
):
    """Odoo computes the grade from the product code and sends nothing for any
    product that is not COFFEE-A/B/C. Refusing the batch would cost its whole
    traceability over a secondary attribute.
    """
    payload = {k: v for k, v in valid_payload.items() if k != "qualityGrade"}

    response = await client.post(URL, json=payload, headers=auth_headers)

    assert response.status_code == 201
    assert response.json()["qualityGrade"] is None


async def test_should_normalise_the_grade_to_our_own_casing(
    client, auth_headers, valid_payload
):
    """Odoo's Selection exports `a`, our shared vocabulary is `A`. Converting
    case at the boundary is the rule already recorded for `batch_status`.
    """
    response = await client.post(URL, json=valid_payload, headers=auth_headers)

    assert response.json()["qualityGrade"] == "A"


async def test_should_treat_an_empty_grade_as_no_grade(
    client, auth_headers, valid_payload
):
    """An empty string is what a file exports when Odoo computed nothing; it
    must not become a grade of its own.
    """
    response = await client.post(
        URL, json={**valid_payload, "qualityGrade": "  "}, headers=auth_headers
    )

    assert response.status_code == 201
    assert response.json()["qualityGrade"] is None


async def test_should_reject_a_grade_outside_the_shared_vocabulary(
    client, auth_headers, valid_payload
):
    """No CHECK constraint backs `quality_grade` in the database yet, so this
    validator is the only thing keeping an invented grade out.
    """
    response = await client.post(
        URL, json={**valid_payload, "qualityGrade": "Z"}, headers=auth_headers
    )

    assert response.status_code == 422
    assert response.json()["detail"]["code"] == "schema_invalid"


# --- authentication --------------------------------------------------------


async def test_should_reject_a_request_without_api_key(client, valid_payload):
    response = await client.post(URL, json=valid_payload)

    assert response.status_code == 401
    assert response.json()["detail"]["code"] == "invalid_api_key"


async def test_should_reject_a_wrong_api_key(client, valid_payload):
    response = await client.post(
        URL, json=valid_payload, headers={"X-API-Key": API_KEY + "-wrong"}
    )

    assert response.status_code == 401


async def test_should_not_persist_anything_when_the_api_key_is_missing(
    client, session, valid_payload
):
    await client.post(URL, json=valid_payload)

    assert await _stored_batch(session, NEW_BATCH_REF) is None


async def test_should_refuse_to_serve_when_the_secret_is_not_configured(
    client, auth_headers, valid_payload, monkeypatch
):
    """An unset variable is a deployment mistake, and it should look like one. A
    route that writes to the database must never fall back to open.
    """
    monkeypatch.delenv(API_KEY_ENV_VAR)

    with pytest.raises(RuntimeError, match=API_KEY_ENV_VAR):
        await client.post(URL, json=valid_payload, headers=auth_headers)
