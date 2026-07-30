"""Test wiring for POST /api/batches.

`app.models` and `app.db` are owned by another dev and do not exist yet. Until
they land, this module registers minimal stand-ins matching the agreed
integration contract, so the endpoint stays verifiable in isolation.

To integrate: delete the "contract stand-ins" block below. Nothing under `app/`
needs to change.
"""

import sys
from datetime import datetime
from types import ModuleType

import pytest
import pytest_asyncio
from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from httpx import ASGITransport, AsyncClient
from sqlalchemy import Boolean, DateTime, ForeignKey, String
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column
from sqlalchemy.pool import StaticPool

# --- contract stand-ins ----------------------------------------------------


class Base(DeclarativeBase):
    pass


class Farm(Base):
    __tablename__ = "farm"

    id: Mapped[int] = mapped_column(primary_key=True)


class Warehouse(Base):
    __tablename__ = "warehouse"

    id: Mapped[int] = mapped_column(primary_key=True)


class User(Base):
    __tablename__ = "app_user"

    id: Mapped[int] = mapped_column(primary_key=True)


class Batch(Base):
    __tablename__ = "batch"

    id: Mapped[int] = mapped_column(primary_key=True)
    farm_id: Mapped[int] = mapped_column(ForeignKey("farm.id"))
    warehouse_id: Mapped[int] = mapped_column(ForeignKey("warehouse.id"))
    user_id: Mapped[int] = mapped_column(ForeignKey("app_user.id"))
    quality: Mapped[str] = mapped_column(String)
    # timestamptz in the real schema; SQLite has no tz-aware storage.
    entered_at: Mapped[datetime] = mapped_column(DateTime)
    status: Mapped[str] = mapped_column(String)
    is_compliant: Mapped[bool] = mapped_column(Boolean)


def _register_contract_modules() -> None:
    models = ModuleType("app.models")
    models.Farm = Farm
    models.Warehouse = Warehouse
    models.User = User
    models.Batch = Batch
    sys.modules["app.models"] = models

    db = ModuleType("app.db")

    async def get_session() -> None:  # replaced by a dependency override
        raise NotImplementedError

    db.get_session = get_session
    sys.modules["app.db"] = db


_register_contract_modules()

# Imported only after the stand-ins are registered.
from app.db import get_session  # noqa: E402
from app.errors import validation_error_handler  # noqa: E402
from app.routers.batches import router  # noqa: E402
from app.security import CurrentUser, get_current_user  # noqa: E402

SEEDED_FARM_ID = 1
OTHER_FARM_ID = 2
CALLER = CurrentUser(id=7, warehouse_id=3)


@pytest.fixture
def valid_payload() -> dict[str, object]:
    return {"farm_id": SEEDED_FARM_ID, "quality": "grade_1_specialty"}


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
                Farm(id=SEEDED_FARM_ID),
                Farm(id=OTHER_FARM_ID),
                Warehouse(id=CALLER.warehouse_id),
                User(id=CALLER.id),
            ]
        )
        await db_session.commit()
        yield db_session

    await engine.dispose()


@pytest_asyncio.fixture
async def client(session):
    app = FastAPI()
    app.include_router(router)
    app.add_exception_handler(RequestValidationError, validation_error_handler)
    app.dependency_overrides[get_session] = lambda: session
    app.dependency_overrides[get_current_user] = lambda: CALLER

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as http_client:
        yield http_client
