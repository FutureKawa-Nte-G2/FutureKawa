"""GET /api/notifications et PATCH /api/notifications/{id}/read.

Ce que ces tests verrouillent : une notification appartient à un entrepôt, et
l'entrepôt fait partie de la recherche — pas d'une vérification faite après
coup. C'est ce qui empêche la cloche d'un entrepôt d'afficher celles d'un autre.
"""

import uuid
from datetime import datetime
from decimal import Decimal

import pytest
from sqlalchemy import select

from app.models import Batch, Notification, Order, Warehouse
from tests.conftest import (
    API_KEY,
    BATCH_ID,
    BATCH_REF,
    COUNTRY_ID,
    WAREHOUSE_ID,
    WAREHOUSE_REF,
)

pytestmark = pytest.mark.asyncio

URL = "/api/notifications"

OTHER_WAREHOUSE_ID = uuid.UUID("00000000-0000-0000-0000-0000000000e2")
OTHER_WAREHOUSE_REF = "BR-ENT-02"
ORDER_ID = uuid.UUID("00000000-0000-0000-0000-0000000000d1")
ORDER_REFERENCE = "CMD-2026-0001"


def _at(day: int, hour: int = 12) -> datetime:
    return datetime(2026, 8, day, hour, 0, 0)


def _batch_notification(
    notification_id: uuid.UUID,
    created_at: datetime,
    read_at: datetime | None = None,
    warehouse_id: uuid.UUID = WAREHOUSE_ID,
    batch_id: uuid.UUID = BATCH_ID,
) -> Notification:
    return Notification(
        notification_id=notification_id,
        warehouse_id=warehouse_id,
        notification_type="batch_non_compliant",
        batch_id=batch_id,
        order_id=None,
        created_at=created_at,
        read_at=read_at,
    )


async def _add_order(session) -> None:
    session.add(
        Order(
            id=ORDER_ID,
            odoo_order_id=42,
            order_reference=ORDER_REFERENCE,
            order_date=_at(1),
            client_name="Café de Paris",
            country_id=COUNTRY_ID,
            status="confirmed",
        )
    )
    await session.commit()


async def _add_other_warehouse(session) -> None:
    session.add(
        Warehouse(
            warehouse_id=OTHER_WAREHOUSE_ID,
            country_id=COUNTRY_ID,
            warehouse_name="Entrepôt de Campinas",
            warehouse_ref=OTHER_WAREHOUSE_REF,
        )
    )
    await session.commit()


# --- lecture ----------------------------------------------------------------


async def test_should_list_a_warehouse_notifications_newest_first(session, client):
    session.add_all(
        [
            _batch_notification(uuid.uuid4(), _at(10)),
            _batch_notification(uuid.uuid4(), _at(12)),
            _batch_notification(uuid.uuid4(), _at(11)),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    dates = [n["createdAt"][:10] for n in body["notifications"]]
    assert dates == ["2026-08-12", "2026-08-11", "2026-08-10"]


async def test_should_never_show_another_warehouse_notifications(session, client):
    """Le test qui compte : la cloche d'un entrepôt ne voit que les siennes."""
    await _add_other_warehouse(session)
    session.add_all(
        [
            _batch_notification(uuid.uuid4(), _at(10)),
            _batch_notification(uuid.uuid4(), _at(11), warehouse_id=OTHER_WAREHOUSE_ID),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert len(body["notifications"]) == 1
    assert body["unreadCount"] == 1


async def test_should_filter_on_unread_when_asked(session, client):
    session.add_all(
        [
            _batch_notification(uuid.uuid4(), _at(10), read_at=_at(10, 18)),
            _batch_notification(uuid.uuid4(), _at(11)),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF, "unread": True})).json()

    assert len(body["notifications"]) == 1
    assert body["notifications"][0]["readAt"] is None


async def test_should_count_every_unread_even_when_the_page_is_limited(session, client):
    """Le badge annonce ce qui attend, pas ce que la page a renvoyé."""
    session.add_all([_batch_notification(uuid.uuid4(), _at(day)) for day in range(1, 6)])
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF, "limit": 2})).json()

    assert len(body["notifications"]) == 2
    assert body["unreadCount"] == 5


async def test_should_compose_the_message_from_the_batch_reference(session, client):
    session.add(_batch_notification(uuid.uuid4(), _at(10)))
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert body["notifications"][0]["message"] == f"Lot {BATCH_REF} non conforme"
    assert body["notifications"][0]["batchId"] == str(BATCH_ID)


async def test_should_compose_the_message_from_the_order_reference(session, client):
    await _add_order(session)
    session.add(
        Notification(
            notification_id=uuid.uuid4(),
            warehouse_id=WAREHOUSE_ID,
            notification_type="order_received",
            batch_id=None,
            order_id=ORDER_ID,
            created_at=_at(10),
        )
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert body["notifications"][0]["message"] == f"Commande {ORDER_REFERENCE} à préparer"
    assert body["notifications"][0]["orderId"] == str(ORDER_ID)
    assert body["notifications"][0]["batchId"] is None


async def test_should_answer_404_for_an_unknown_warehouse_not_an_empty_list(client):
    """Une référence inconnue et un entrepôt calme ne se ressemblent pas."""
    response = await client.get(URL, params={"warehouse_ref": "NEXISTE-PAS"})

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "warehouse_ref_unknown"


async def test_should_answer_200_with_an_empty_list_for_a_quiet_warehouse(client):
    response = await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})

    assert response.status_code == 200
    assert response.json() == {"notifications": [], "unreadCount": 0}


async def test_should_require_the_warehouse_reference(client):
    assert (await client.get(URL)).status_code == 422


# --- accusé de lecture ------------------------------------------------------


async def test_should_stamp_a_notification_as_read(session, client, auth_headers):
    notification_id = uuid.uuid4()
    session.add(_batch_notification(notification_id, _at(10)))
    await session.commit()

    response = await client.patch(
        f"{URL}/{notification_id}/read",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert response.status_code == 200
    assert response.json()["readAt"] is not None

    stored = await session.scalar(
        select(Notification).where(Notification.notification_id == notification_id)
    )
    assert stored.read_at is not None


async def test_should_not_move_the_timestamp_of_an_already_read_notification(
    session, client, auth_headers
):
    """La cloche déclenche ça à chaque clic ; la question est « vue quand ? »."""
    notification_id = uuid.uuid4()
    first_seen = _at(10, 18)
    session.add(_batch_notification(notification_id, _at(10), read_at=first_seen))
    await session.commit()

    response = await client.patch(
        f"{URL}/{notification_id}/read",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert response.status_code == 200
    assert response.json()["readAt"].startswith("2026-08-10T18:00")


async def test_should_answer_404_when_the_notification_belongs_to_another_warehouse(
    session, client, auth_headers
):
    """404 et non 403 : la route ne confirme pas l'existence d'un id."""
    await _add_other_warehouse(session)
    notification_id = uuid.uuid4()
    session.add(_batch_notification(notification_id, _at(10), warehouse_id=OTHER_WAREHOUSE_ID))
    await session.commit()

    response = await client.patch(
        f"{URL}/{notification_id}/read",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "notification_not_found"

    # Et elle reste non lue pour son véritable destinataire.
    stored = await session.scalar(
        select(Notification).where(Notification.notification_id == notification_id)
    )
    assert stored.read_at is None


async def test_should_answer_404_for_an_unknown_notification(client, auth_headers):
    response = await client.patch(
        f"{URL}/{uuid.uuid4()}/read",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert response.status_code == 404


async def test_should_refuse_to_mark_read_without_the_api_key(session, client):
    """Cette route écrit : elle ne se replie jamais sur un accès ouvert."""
    notification_id = uuid.uuid4()
    session.add(_batch_notification(notification_id, _at(10)))
    await session.commit()

    response = await client.patch(
        f"{URL}/{notification_id}/read", params={"warehouse_ref": WAREHOUSE_REF}
    )

    assert response.status_code == 401


async def test_should_leave_the_listing_open_to_the_relay(session, client):
    """La lecture reste ouverte, comme GET /api/measurements."""
    session.add(_batch_notification(uuid.uuid4(), _at(10)))
    await session.commit()

    assert (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).status_code == 200


async def test_should_serve_camel_case_on_the_wire(session, client):
    session.add(_batch_notification(uuid.uuid4(), _at(10)))
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert "unreadCount" in body
    first = body["notifications"][0]
    for key in ("notificationId", "notificationType", "createdAt", "readAt", "batchId"):
        assert key in first, key
    assert "notification_id" not in first
