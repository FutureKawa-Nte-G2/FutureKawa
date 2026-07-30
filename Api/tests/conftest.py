"""Test wiring for the notification endpoints.

`app.models` and `app.db` are owned by another dev and do not exist yet. Until
they land, this module registers minimal stand-ins matching the agreed
integration contract, so the endpoints stay verifiable in isolation.

To integrate: delete the "contract stand-ins" block below. Nothing under `app/`
needs to change.
"""

import sys
from datetime import datetime
from types import ModuleType

import pytest
import pytest_asyncio
from fastapi import FastAPI
from httpx import ASGITransport, AsyncClient
from sqlalchemy import DateTime, ForeignKey, String
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column
from sqlalchemy.pool import StaticPool

# --- contract stand-ins ----------------------------------------------------


class Base(DeclarativeBase):
    pass


class Warehouse(Base):
    __tablename__ = "warehouse"

    id: Mapped[int] = mapped_column(primary_key=True)


class Notification(Base):
    __tablename__ = "notification"

    id: Mapped[int] = mapped_column(primary_key=True)
    warehouse_id: Mapped[int] = mapped_column(ForeignKey("warehouse.id"))
    message: Mapped[str] = mapped_column(String)
    # timestamptz in the real schema; SQLite has no tz-aware storage.
    created_at: Mapped[datetime] = mapped_column(DateTime)
    read_at: Mapped[datetime | None] = mapped_column(DateTime, nullable=True)


def _register_contract_modules() -> None:
    models = ModuleType("app.models")
    models.Warehouse = Warehouse
    models.Notification = Notification
    sys.modules["app.models"] = models

    db = ModuleType("app.db")

    async def get_session() -> None:  # replaced by a dependency override
        raise NotImplementedError

    db.get_session = get_session
    sys.modules["app.db"] = db


_register_contract_modules()

# Imported only after the stand-ins are registered.
from app.db import get_session  # noqa: E402
from app.routers.notifications import router  # noqa: E402
from app.security import CurrentUser, get_current_user  # noqa: E402

CALLER = CurrentUser(id=7, warehouse_id=3)
OTHER_WAREHOUSE_ID = 4
EMPTY_WAREHOUSE_ID = 5

UNREAD_OLDEST_ID = 1
UNREAD_NEWEST_ID = 2
ALREADY_READ_ID = 3
OTHER_WAREHOUSE_NOTIFICATION_ID = 4

# Newest first, as the endpoint must return them.
CALLER_IDS_NEWEST_FIRST = [UNREAD_NEWEST_ID, ALREADY_READ_ID, UNREAD_OLDEST_ID]
CALLER_UNREAD_IDS_NEWEST_FIRST = [UNREAD_NEWEST_ID, UNREAD_OLDEST_ID]


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
                Warehouse(id=CALLER.warehouse_id),
                Warehouse(id=OTHER_WAREHOUSE_ID),
                Warehouse(id=EMPTY_WAREHOUSE_ID),
                Notification(
                    id=UNREAD_OLDEST_ID,
                    warehouse_id=CALLER.warehouse_id,
                    message="Lot non conforme a retirer du stock",
                    created_at=datetime(2026, 7, 28, 9, 0),
                ),
                Notification(
                    id=UNREAD_NEWEST_ID,
                    warehouse_id=CALLER.warehouse_id,
                    message="Nouvelle commande a preparer",
                    created_at=datetime(2026, 7, 30, 8, 0),
                ),
                Notification(
                    id=ALREADY_READ_ID,
                    warehouse_id=CALLER.warehouse_id,
                    message="Lot 42 expedie",
                    created_at=datetime(2026, 7, 29, 12, 0),
                    read_at=datetime(2026, 7, 29, 13, 0),
                ),
                Notification(
                    id=OTHER_WAREHOUSE_NOTIFICATION_ID,
                    warehouse_id=OTHER_WAREHOUSE_ID,
                    message="Ne doit jamais sortir de son entrepot",
                    created_at=datetime(2026, 7, 30, 9, 0),
                ),
            ]
        )
        await db_session.commit()
        yield db_session

    await engine.dispose()


@pytest_asyncio.fixture
async def client(session):
    app = FastAPI()
    app.include_router(router)
    app.dependency_overrides[get_session] = lambda: session
    app.dependency_overrides[get_current_user] = lambda: CALLER

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as http_client:
        yield http_client
