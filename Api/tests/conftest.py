"""Test wiring for GET /api/alerts.

`app.models` and `app.db` are owned by another dev and do not exist yet. Until
they land, this module registers minimal stand-ins matching the agreed
integration contract, so the endpoint stays verifiable in isolation.

To integrate: delete the "contract stand-ins" block below. Nothing under `app/`
needs to change.

The stand-ins follow the corrected data model (`Documentation/diagrammes/
er_chart_corrected.md`): there is no NOTIFICATION table, ALERT carries what the
UI displays, and an alert points either at a warehouse (`condition`) or at a
batch (`expiration`).
"""

import sys
from datetime import datetime
from decimal import Decimal
from types import ModuleType

import pytest
import pytest_asyncio
from fastapi import FastAPI
from httpx import ASGITransport, AsyncClient
from sqlalchemy import DateTime, ForeignKey, Numeric, String
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column
from sqlalchemy.pool import StaticPool

# --- contract stand-ins ----------------------------------------------------


class Base(DeclarativeBase):
    pass


class Warehouse(Base):
    __tablename__ = "warehouse"

    id: Mapped[int] = mapped_column(primary_key=True)
    external_ref: Mapped[str] = mapped_column(String, unique=True)


class Batch(Base):
    __tablename__ = "batch"

    id: Mapped[int] = mapped_column(primary_key=True)
    batch_ref: Mapped[str] = mapped_column(String, unique=True)
    warehouse_id: Mapped[int] = mapped_column(ForeignKey("warehouse.id"))


class Alert(Base):
    __tablename__ = "alert"

    id: Mapped[int] = mapped_column(primary_key=True)
    type: Mapped[str] = mapped_column(String)
    state: Mapped[str] = mapped_column(String)
    # Exactly one of the two is filled, depending on `type`.
    warehouse_id: Mapped[int | None] = mapped_column(
        ForeignKey("warehouse.id"), nullable=True
    )
    batch_id: Mapped[int | None] = mapped_column(ForeignKey("batch.id"), nullable=True)
    value: Mapped[Decimal | None] = mapped_column(Numeric(10, 2), nullable=True)
    # timestamptz in the real schema; SQLite has no tz-aware storage.
    created_at: Mapped[datetime] = mapped_column(DateTime)
    resolved_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


def _register_contract_modules() -> None:
    models = ModuleType("app.models")
    models.Warehouse = Warehouse
    models.Batch = Batch
    models.Alert = Alert
    sys.modules["app.models"] = models

    db = ModuleType("app.db")

    async def get_session() -> None:  # replaced by a dependency override
        raise NotImplementedError

    db.get_session = get_session
    sys.modules["app.db"] = db


_register_contract_modules()

# Imported only after the stand-ins are registered.
from app.db import get_session  # noqa: E402
from app.routers.alerts import router  # noqa: E402
from app.security import API_KEY_ENV_VAR  # noqa: E402

# --- seeded world ----------------------------------------------------------

API_KEY = "head-office-secret"

WAREHOUSE_A_ID = 1
WAREHOUSE_A_REF = "BR-ENT-01"
WAREHOUSE_B_ID = 2
WAREHOUSE_B_REF = "BR-ENT-02"

BATCH_ID = 1
BATCH_REF = "BR-2026-00042"

# A condition alert: points at its warehouse, names no batch.
CONDITION_ALERT_ID = 1
CONDITION_ALERT_AT = datetime(2026, 7, 28, 9, 0)

# An expiration alert: points at a batch, and reaches the warehouse through it.
EXPIRATION_ALERT_ID = 2
EXPIRATION_ALERT_AT = datetime(2026, 7, 30, 8, 0)

# An alert already dealt with, in another warehouse.
RESOLVED_ALERT_ID = 3
RESOLVED_ALERT_AT = datetime(2026, 7, 29, 12, 0)
RESOLVED_AT = datetime(2026, 7, 29, 13, 0)

ALERT_IDS_NEWEST_FIRST = [EXPIRATION_ALERT_ID, RESOLVED_ALERT_ID, CONDITION_ALERT_ID]
ACTIVE_ALERT_IDS_NEWEST_FIRST = [EXPIRATION_ALERT_ID, CONDITION_ALERT_ID]


@pytest.fixture
def api_key(monkeypatch) -> str:
    monkeypatch.setenv(API_KEY_ENV_VAR, API_KEY)
    return API_KEY


@pytest_asyncio.fixture
async def session():
    engine = create_async_engine(
        "sqlite+aiosqlite:///:memory:",
        poolclass=StaticPool,
        connect_args={"check_same_thread": False},
    )
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as db_session:
        db_session.add_all(
            [
                Warehouse(id=WAREHOUSE_A_ID, external_ref=WAREHOUSE_A_REF),
                Warehouse(id=WAREHOUSE_B_ID, external_ref=WAREHOUSE_B_REF),
            ]
        )
        await db_session.flush()

        db_session.add(
            Batch(id=BATCH_ID, batch_ref=BATCH_REF, warehouse_id=WAREHOUSE_A_ID)
        )
        await db_session.flush()

        db_session.add_all(
            [
                Alert(
                    id=CONDITION_ALERT_ID,
                    type="condition",
                    state="active",
                    warehouse_id=WAREHOUSE_A_ID,
                    value=Decimal("27.50"),
                    created_at=CONDITION_ALERT_AT,
                ),
                Alert(
                    id=EXPIRATION_ALERT_ID,
                    type="expiration",
                    state="active",
                    batch_id=BATCH_ID,
                    value=Decimal("400.00"),
                    created_at=EXPIRATION_ALERT_AT,
                ),
                Alert(
                    id=RESOLVED_ALERT_ID,
                    type="condition",
                    state="resolved",
                    warehouse_id=WAREHOUSE_B_ID,
                    value=Decimal("31.00"),
                    created_at=RESOLVED_ALERT_AT,
                    resolved_at=RESOLVED_AT,
                ),
            ]
        )
        await db_session.commit()
        yield db_session

    await engine.dispose()


@pytest_asyncio.fixture
async def client(session, api_key):
    app = FastAPI()
    app.include_router(router)
    app.dependency_overrides[get_session] = lambda: session

    transport = ASGITransport(app=app)
    async with AsyncClient(
        transport=transport,
        base_url="http://test",
        headers={"X-API-Key": api_key},
    ) as http_client:
        yield http_client
