from datetime import UTC, datetime

import pytest
from sqlalchemy import select

from app.security import CurrentUser, get_current_user
from app.services.notifications import (
    NotificationNotFoundError,
    list_notifications,
    mark_notification_read,
)
from tests.conftest import (
    ALREADY_READ_ID,
    CALLER,
    CALLER_IDS_NEWEST_FIRST,
    CALLER_UNREAD_IDS_NEWEST_FIRST,
    EMPTY_WAREHOUSE_ID,
    OTHER_WAREHOUSE_NOTIFICATION_ID,
    UNREAD_NEWEST_ID,
    Notification,
)

pytestmark = pytest.mark.asyncio


# --- GET /api/notifications ------------------------------------------------


async def test_should_list_notifications_newest_first(client):
    response = await client.get("/api/notifications")

    assert response.status_code == 200
    assert [item["id"] for item in response.json()] == CALLER_IDS_NEWEST_FIRST


async def test_should_only_list_notifications_of_the_caller_warehouse(client):
    response = await client.get("/api/notifications")

    returned_ids = [item["id"] for item in response.json()]
    assert OTHER_WAREHOUSE_NOTIFICATION_ID not in returned_ids
    assert {item["warehouse_id"] for item in response.json()} == {CALLER.warehouse_id}


async def test_should_only_list_unread_when_unread_is_true(client):
    response = await client.get("/api/notifications?unread=true")

    assert [item["id"] for item in response.json()] == CALLER_UNREAD_IDS_NEWEST_FIRST
    assert all(item["read_at"] is None for item in response.json())


async def test_should_list_read_and_unread_when_unread_is_not_requested(client):
    response = await client.get("/api/notifications")

    assert ALREADY_READ_ID in [item["id"] for item in response.json()]


async def test_should_ignore_warehouse_id_from_the_query_string(client):
    """The documented URL carries warehouse_id, but the token decides."""
    response = await client.get("/api/notifications?warehouse_id=4")

    assert response.status_code == 200
    assert [item["id"] for item in response.json()] == CALLER_IDS_NEWEST_FIRST
    assert OTHER_WAREHOUSE_NOTIFICATION_ID not in [
        item["id"] for item in response.json()
    ]


async def test_should_return_every_field_of_a_notification(client):
    response = await client.get("/api/notifications")

    assert set(response.json()[0]) == {
        "id",
        "warehouse_id",
        "message",
        "created_at",
        "read_at",
    }


async def test_should_return_empty_list_when_warehouse_has_no_notification(session):
    caller = CurrentUser(id=8, warehouse_id=EMPTY_WAREHOUSE_ID)

    notifications = await list_notifications(session, caller)

    assert notifications == []


# --- PATCH /api/notifications/{id}/read ------------------------------------


async def test_should_mark_notification_as_read(client):
    response = await client.patch(f"/api/notifications/{UNREAD_NEWEST_ID}/read")

    assert response.status_code == 200
    assert response.json()["read_at"] is not None


async def test_should_persist_read_at_when_marking_as_read(client, session):
    await client.patch(f"/api/notifications/{UNREAD_NEWEST_ID}/read")

    read_at = await session.scalar(
        select(Notification.read_at).where(Notification.id == UNREAD_NEWEST_ID)
    )
    assert read_at is not None


async def test_should_remove_notification_from_unread_list_once_read(client):
    await client.patch(f"/api/notifications/{UNREAD_NEWEST_ID}/read")

    response = await client.get("/api/notifications?unread=true")

    assert UNREAD_NEWEST_ID not in [item["id"] for item in response.json()]


async def test_should_keep_original_read_at_when_marked_read_twice(client):
    first = await client.patch(f"/api/notifications/{UNREAD_NEWEST_ID}/read")

    second = await client.patch(f"/api/notifications/{UNREAD_NEWEST_ID}/read")

    assert second.status_code == 200
    assert second.json()["read_at"] == first.json()["read_at"]


async def test_should_stamp_read_at_at_the_given_instant(session):
    read_at = datetime(2026, 7, 30, 10, 15, 0, tzinfo=UTC)

    notification = await mark_notification_read(
        session, UNREAD_NEWEST_ID, CALLER, now=read_at
    )

    # SQLite drops tzinfo on read back; timestamptz keeps it in the real schema.
    assert notification.read_at.replace(tzinfo=UTC) == read_at


async def test_should_default_read_at_to_now_when_not_injected(session):
    before = datetime.now(UTC).replace(tzinfo=None)

    notification = await mark_notification_read(session, UNREAD_NEWEST_ID, CALLER)

    assert notification.read_at.replace(tzinfo=None) >= before


async def test_should_return_404_when_notification_does_not_exist(client):
    response = await client.patch("/api/notifications/4242/read")

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "notification_not_found"


async def test_should_return_404_when_notification_belongs_to_another_warehouse(client):
    """404 rather than 403: the caller must not learn the id exists."""
    response = await client.patch(
        f"/api/notifications/{OTHER_WAREHOUSE_NOTIFICATION_ID}/read"
    )

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "notification_not_found"


async def test_should_not_mark_read_a_notification_of_another_warehouse(client, session):
    await client.patch(f"/api/notifications/{OTHER_WAREHOUSE_NOTIFICATION_ID}/read")

    read_at = await session.scalar(
        select(Notification.read_at).where(
            Notification.id == OTHER_WAREHOUSE_NOTIFICATION_ID
        )
    )
    assert read_at is None


async def test_should_raise_when_notification_is_outside_the_caller_warehouse(session):
    with pytest.raises(NotificationNotFoundError):
        await mark_notification_read(
            session, OTHER_WAREHOUSE_NOTIFICATION_ID, CALLER
        )


# --- auth seam -------------------------------------------------------------


async def test_should_refuse_to_serve_until_jwt_decoding_is_wired():
    """Guards the seam: the endpoints must not fall back to a placeholder user,
    which would expose another warehouse's notifications.
    """
    with pytest.raises(NotImplementedError):
        await get_current_user()
