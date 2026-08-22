"""Test wiring for POST /api/batches.

`app.models` and `app.db` are owned by another dev and do not exist yet. Until
they land, this module registers minimal stand-ins matching the agreed
integration contract, so the endpoint stays verifiable in isolation.

To integrate: delete the "contract stand-ins" block below. Nothing under `app/`
needs to change.

The stand-ins follow the corrected data model (`Documentation/diagrammes/
er_chart_corrected.md`): batches arrive from ERP reception files, so BATCH
carries `batch_ref`, `stored_at` and `shipped_at`, and no longer references the
USER who typed it in.
"""

import sys
from datetime import date
from types import ModuleType

import pytest
import pytest_asyncio
from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError
from httpx import ASGITransport, AsyncClient
from sqlalchemy import Date, ForeignKey, String
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column
from sqlalchemy.pool import StaticPool

# --- contract stand-ins ----------------------------------------------------


class Base(DeclarativeBase):
    pass


class Farm(Base):
    __tablename__ = "farm"

    id: Mapped[int] = mapped_column(primary_key=True)
    # The reference the ERP uses to designate this farm in its files.
    external_ref: Mapped[str] = mapped_column(String, unique=True)


class Warehouse(Base):
    __tablename__ = "warehouse"

    id: Mapped[int] = mapped_column(primary_key=True)
    external_ref: Mapped[str] = mapped_column(String, unique=True)


class Batch(Base):
    __tablename__ = "batch"

    id: Mapped[int] = mapped_column(primary_key=True)
    # UNIQUE is not decoration here: it is what stops a replayed reception file
    # from creating the same batch twice. `app/models.py` must carry it.
    batch_ref: Mapped[str] = mapped_column(String, unique=True)
    farm_id: Mapped[int] = mapped_column(ForeignKey("farm.id"))
    warehouse_id: Mapped[int] = mapped_column(ForeignKey("warehouse.id"))
    quality_grade: Mapped[str | None] = mapped_column(String, nullable=True)
    stored_at: Mapped[date] = mapped_column(Date)
    # NULL means still in stock: this is the field the FIFO query reads.
    shipped_at: Mapped[date | None] = mapped_column(Date, nullable=True)
    status: Mapped[str] = mapped_column(String)


def _register_contract_modules() -> None:
    models = ModuleType("app.models")
    models.Farm = Farm
    models.Warehouse = Warehouse
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

SEEDED_FARM_REF = "BR-EXP-01"
OTHER_FARM_REF = "BR-EXP-02"
SEEDED_WAREHOUSE_REF = "BR-ENT-01"
OTHER_WAREHOUSE_REF = "BR-ENT-02"

SEEDED_FARM_ID = 1
OTHER_FARM_ID = 2
SEEDED_WAREHOUSE_ID = 10
OTHER_WAREHOUSE_ID = 20

STORED_AT = date(2026, 7, 30)


@pytest.fixture
def valid_payload() -> dict[str, object]:
    return {
        "batch_ref": "BR-2026-00042",
        "farm_ref": SEEDED_FARM_REF,
        "warehouse_ref": SEEDED_WAREHOUSE_REF,
        "stored_at": STORED_AT.isoformat(),
        "quality_grade": "grade_1_specialty",
    }


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
                Farm(id=SEEDED_FARM_ID, external_ref=SEEDED_FARM_REF),
                Farm(id=OTHER_FARM_ID, external_ref=OTHER_FARM_REF),
                Warehouse(id=SEEDED_WAREHOUSE_ID, external_ref=SEEDED_WAREHOUSE_REF),
                Warehouse(id=OTHER_WAREHOUSE_ID, external_ref=OTHER_WAREHOUSE_REF),
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

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as http_client:
        yield http_client
