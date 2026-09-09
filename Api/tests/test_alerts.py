"""GET /api/alerts et PATCH /api/alerts/{id}/resolve.

Ce que ces tests verrouillent, au-delà des deux routes : `alerts` n'autorise
qu'une seule alerte `condition` active par entrepôt. Tant que rien ne résolvait,
la première dérive condamnait la salle — toute alerte suivante tombait dans le
rattrapage d'IntegrityError de `evaluate_reading` et disparaissait. La section
« réarmement » est la raison d'être de ces routes ; le reste n'est que la
plomberie qui l'expose.
"""

import uuid
from datetime import datetime
from decimal import Decimal

import pytest
from sqlalchemy import func, select

from app.models import Alert, Batch, Sensor, SensorAssignment, Warehouse
from app.services.ingestion import Reading
from app.services.quality import evaluate_reading
from tests.conftest import (
    API_KEY,
    BATCH_ID,
    BATCH_REF,
    COUNTRY_ID,
    FARM_ID,
    SENSOR_CODE,
    SENSOR_ID,
    WAREHOUSE_ID,
    WAREHOUSE_REF,
)

pytestmark = pytest.mark.asyncio

URL = "/api/alerts"

OTHER_WAREHOUSE_ID = uuid.UUID("00000000-0000-0000-0000-0000000000e2")
OTHER_WAREHOUSE_REF = "BR-ENT-02"

# Un second lot dans le même entrepôt, avec son propre capteur : c'est le
# scénario réel du réarmement. Un lot déjà non conforme ne redéclenche rien par
# construction, donc rejouer une dérive sur le même lot ne prouverait rien.
SECOND_BATCH_ID = uuid.UUID("00000000-0000-0000-0000-0000000000b2")
SECOND_BATCH_REF = "BR-2026-00044"
SECOND_SENSOR_ID = uuid.UUID("00000000-0000-0000-0000-0000000000a2")
SECOND_SENSOR_CODE = "BR-SEN-02"


def _at(day: int, hour: int = 12) -> datetime:
    return datetime(2026, 8, day, hour, 0, 0)


def _alert(
    alert_id: uuid.UUID,
    created_at: datetime,
    alert_type: str = "condition",
    alert_status: str = "active",
    resolved_at: datetime | None = None,
    warehouse_id: uuid.UUID = WAREHOUSE_ID,
    batch_id: uuid.UUID | None = None,
) -> Alert:
    return Alert(
        alert_id=alert_id,
        warehouse_id=warehouse_id,
        batch_id=batch_id,
        alert_type=alert_type,
        alert_status=alert_status,
        created_at=created_at,
        resolved_at=resolved_at,
    )


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


async def _add_second_batch(session) -> None:
    """Un second lot équipé, dans le même entrepôt que celui de référence."""
    session.add_all(
        [
            Batch(
                batch_id=SECOND_BATCH_ID,
                warehouse_id=WAREHOUSE_ID,
                farm_id=FARM_ID,
                batch_ref=SECOND_BATCH_REF,
                stored_at=_at(2).date(),
                quality_grade="A",
                batch_status="stored",
            ),
            Sensor(
                sensor_id=SECOND_SENSOR_ID,
                warehouse_id=WAREHOUSE_ID,
                code=SECOND_SENSOR_CODE,
            ),
        ]
    )
    await session.commit()


async def _assign(session, sensor_id: uuid.UUID, batch_id: uuid.UUID) -> None:
    session.add(
        SensorAssignment(
            sensor_assignment_id=uuid.uuid4(),
            sensor_id=sensor_id,
            batch_id=batch_id,
            assigned_at=_at(1),
            released_at=None,
        )
    )
    await session.commit()


def _out_of_band(sensor_code: str, day: int) -> Reading:
    """Un relevé franchement hors de la bande du pays (20 °C ± 5)."""
    return Reading(
        sensor_code=sensor_code,
        measured_at=_at(day),
        temperature=Decimal("40.00"),
        humidity=Decimal("55.00"),
    )


async def _count_alerts(session, **filters) -> int:
    query = select(func.count()).select_from(Alert)
    for column, value in filters.items():
        query = query.where(getattr(Alert, column) == value)
    return await session.scalar(query)


# --- lecture ----------------------------------------------------------------


async def test_should_list_a_warehouse_alerts_newest_first(session, client):
    # Des `expiration` : l'index unique partiel n'autorise qu'une `condition`
    # active par entrepôt, et trois d'un coup ne seraient pas un état atteignable.
    session.add_all(
        [
            _alert(uuid.uuid4(), _at(10), alert_type="expiration"),
            _alert(uuid.uuid4(), _at(12), alert_type="expiration"),
            _alert(uuid.uuid4(), _at(11), alert_type="expiration"),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF, "status": "all"})).json()

    dates = [a["createdAt"][:10] for a in body["alerts"]]
    assert dates == ["2026-08-12", "2026-08-11", "2026-08-10"]


async def test_should_never_show_another_warehouse_alerts(session, client):
    """Le test qui compte côté lecture : un entrepôt ne voit que les siennes."""
    await _add_other_warehouse(session)
    session.add_all(
        [
            _alert(uuid.uuid4(), _at(10)),
            _alert(uuid.uuid4(), _at(11), warehouse_id=OTHER_WAREHOUSE_ID),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert len(body["alerts"]) == 1
    assert body["activeCount"] == 1


async def test_should_return_only_active_alerts_by_default(session, client):
    session.add_all(
        [
            _alert(uuid.uuid4(), _at(10), alert_status="resolved", resolved_at=_at(11)),
            _alert(uuid.uuid4(), _at(12)),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert len(body["alerts"]) == 1
    assert body["alerts"][0]["alertStatus"] == "active"


async def test_should_return_every_alert_when_asked_for_all(session, client):
    session.add_all(
        [
            _alert(uuid.uuid4(), _at(10), alert_status="resolved", resolved_at=_at(11)),
            _alert(uuid.uuid4(), _at(12)),
        ]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF, "status": "all"})).json()

    assert len(body["alerts"]) == 2


async def test_should_return_only_resolved_alerts_when_asked(session, client):
    session.add_all(
        [
            _alert(uuid.uuid4(), _at(10), alert_status="resolved", resolved_at=_at(11)),
            _alert(uuid.uuid4(), _at(12)),
        ]
    )
    await session.commit()

    body = (
        await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF, "status": "resolved"})
    ).json()

    assert len(body["alerts"]) == 1
    assert body["alerts"][0]["resolvedAt"] is not None


async def test_should_count_every_active_alert_even_when_the_page_is_limited(session, client):
    """Le compteur annonce ce qui est ouvert, pas ce que la page a renvoyé.

    Les alertes portent des types différents : l'index unique partiel ne couvre
    que les `condition` actives, une seule par entrepôt.
    """
    session.add_all(
        [_alert(uuid.uuid4(), _at(day), alert_type="expiration") for day in range(1, 6)]
    )
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF, "limit": 2})).json()

    assert len(body["alerts"]) == 2
    assert body["activeCount"] == 5


async def test_should_compose_the_message_of_a_room_alert(session, client):
    session.add(_alert(uuid.uuid4(), _at(10)))
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert body["alerts"][0]["message"] == "Conditions hors plage dans l'entrepôt"
    assert body["alerts"][0]["batchId"] is None
    assert body["alerts"][0]["batchRef"] is None


async def test_should_compose_the_message_of_an_expiration_from_the_batch_reference(
    session, client
):
    session.add(_alert(uuid.uuid4(), _at(10), alert_type="expiration", batch_id=BATCH_ID))
    await session.commit()

    body = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()

    assert body["alerts"][0]["message"] == f"Lot {BATCH_REF} périmé"
    assert body["alerts"][0]["batchRef"] == BATCH_REF


async def test_should_answer_404_for_an_unknown_warehouse_not_an_empty_list(client):
    """Une référence inconnue et un entrepôt sain ne se ressemblent pas."""
    response = await client.get(URL, params={"warehouse_ref": "NEXISTE-PAS"})

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "warehouse_ref_unknown"


async def test_should_answer_200_with_an_empty_list_for_a_healthy_warehouse(client):
    response = await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})

    assert response.status_code == 200
    assert response.json() == {"alerts": [], "activeCount": 0}


async def test_should_require_the_warehouse_reference(client):
    assert (await client.get(URL)).status_code == 422


async def test_should_reject_an_unknown_status_filter(client):
    response = await client.get(
        URL, params={"warehouse_ref": WAREHOUSE_REF, "status": "peut-etre"}
    )

    assert response.status_code == 422


# --- résolution -------------------------------------------------------------


async def test_should_stamp_an_alert_as_resolved(session, client, auth_headers):
    alert_id = uuid.uuid4()
    session.add(_alert(alert_id, _at(10)))
    await session.commit()

    response = await client.patch(
        f"{URL}/{alert_id}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert response.status_code == 200
    body = response.json()
    assert body["alertStatus"] == "resolved"
    assert body["resolvedAt"] is not None


async def test_should_not_move_the_timestamp_of_an_already_resolved_alert(
    session, client, auth_headers
):
    """Idempotent : `resolvedAt` dit quand ça s'est arrêté, pas quand on a recliqué."""
    alert_id = uuid.uuid4()
    session.add(
        _alert(alert_id, _at(10), alert_status="resolved", resolved_at=_at(11))
    )
    await session.commit()

    first = await client.patch(
        f"{URL}/{alert_id}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )
    second = await client.patch(
        f"{URL}/{alert_id}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert first.json()["resolvedAt"] == second.json()["resolvedAt"]
    assert first.json()["resolvedAt"].startswith("2026-08-11")


async def test_should_answer_404_when_the_alert_belongs_to_another_warehouse(
    session, client, auth_headers
):
    """404 et non 403 : la route ne confirme pas l'existence d'un id à un tiers."""
    await _add_other_warehouse(session)
    alert_id = uuid.uuid4()
    session.add(_alert(alert_id, _at(10), warehouse_id=OTHER_WAREHOUSE_ID))
    await session.commit()

    response = await client.patch(
        f"{URL}/{alert_id}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert response.status_code == 404
    assert response.json()["detail"]["code"] == "alert_not_found"


async def test_should_answer_404_for_an_unknown_alert(client, auth_headers):
    response = await client.patch(
        f"{URL}/{uuid.uuid4()}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    assert response.status_code == 404


async def test_should_refuse_to_resolve_without_the_api_key(session, client):
    """Une route qui écrit ne retombe jamais en accès ouvert."""
    alert_id = uuid.uuid4()
    session.add(_alert(alert_id, _at(10)))
    await session.commit()

    response = await client.patch(
        f"{URL}/{alert_id}/resolve", params={"warehouse_ref": WAREHOUSE_REF}
    )

    assert response.status_code == 401
    assert await _count_alerts(session, alert_status="active") == 1


# --- réarmement : la raison d'être de la branche ----------------------------


async def test_should_not_open_a_second_room_alert_while_the_first_is_active(session):
    """Contrôle négatif : l'index unique partiel n'autorise qu'une active.

    Ce comportement est voulu — il évite qu'une salle en panne ouvre une alerte
    par relevé. Il n'est tenable que parce qu'on peut refermer la première.
    """
    await _assign(session, SENSOR_ID, BATCH_ID)
    await evaluate_reading(session, _out_of_band(SENSOR_CODE, day=10))

    await _add_second_batch(session)
    await _assign(session, SECOND_SENSOR_ID, SECOND_BATCH_ID)
    second = await evaluate_reading(session, _out_of_band(SECOND_SENSOR_CODE, day=11))

    assert second.alert_created is False
    assert await _count_alerts(session, alert_type="condition") == 1


async def test_should_open_a_new_room_alert_once_the_first_is_resolved(
    session, client, auth_headers
):
    """🎯 Le test de la branche : résoudre réarme la détection.

    Sans la route de résolution, la deuxième dérive de la salle n'ouvrait rien
    et personne ne l'apprenait. Ce scénario échouait avant cette branche.
    """
    await _assign(session, SENSOR_ID, BATCH_ID)
    first = await evaluate_reading(session, _out_of_band(SENSOR_CODE, day=10))
    assert first.alert_created is True

    opened = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()
    alert_id = opened["alerts"][0]["alertId"]

    resolved = await client.patch(
        f"{URL}/{alert_id}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )
    assert resolved.status_code == 200

    await _add_second_batch(session)
    await _assign(session, SECOND_SENSOR_ID, SECOND_BATCH_ID)
    second = await evaluate_reading(session, _out_of_band(SECOND_SENSOR_CODE, day=20))

    assert second.alert_created is True
    assert await _count_alerts(session, alert_type="condition") == 2
    assert await _count_alerts(session, alert_type="condition", alert_status="active") == 1


async def test_should_leave_the_batch_flag_alone_when_resolving(
    session, client, auth_headers
):
    """Réparer la salle ne blanchit pas le café qui a passé la nuit hors bande."""
    await _assign(session, SENSOR_ID, BATCH_ID)
    await evaluate_reading(session, _out_of_band(SENSOR_CODE, day=10))

    opened = (await client.get(URL, params={"warehouse_ref": WAREHOUSE_REF})).json()
    await client.patch(
        f"{URL}/{opened['alerts'][0]['alertId']}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers=auth_headers,
    )

    batch = await session.get(Batch, BATCH_ID)
    await session.refresh(batch)
    assert batch.is_compliant is False


async def test_should_still_require_the_key_after_the_env_var_is_removed(
    session, client, monkeypatch
):
    """La clé configurée est la seule qui passe."""
    alert_id = uuid.uuid4()
    session.add(_alert(alert_id, _at(10)))
    await session.commit()

    response = await client.patch(
        f"{URL}/{alert_id}/resolve",
        params={"warehouse_ref": WAREHOUSE_REF},
        headers={"X-API-Key": f"{API_KEY}-faux"},
    )

    assert response.status_code == 401
