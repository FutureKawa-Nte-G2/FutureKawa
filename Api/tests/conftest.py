"""Shared test wiring for the country API.

Every test runs against the real models from `app.models`, on SQLite in memory:
no container to start, and each test gets a database of its own. This replaces
the per-branch stand-ins that #13, #25 and #32 each had to invent while
`app.models` did not exist — they had already drifted apart from one another.

What SQLite does not reproduce, and what to keep in mind when reading a green
suite:

  * no timezone-aware storage, so a `timestamptz` comes back naive;
  * `numeric(5,2)` is not enforced, so an over-precise decimal is not rounded;
  * partial indexes work, but through `sqlite_where`, declared next to the
    `postgresql_where` in `app/models.py`.
"""

import uuid
from collections.abc import AsyncGenerator
from datetime import date
from decimal import Decimal

import pytest
import pytest_asyncio
from fastapi import FastAPI
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine
from sqlalchemy.pool import StaticPool

from app.db import get_session
from app.main import create_app
from app.models import Base, Batch, Country, Farm, Sensor, Warehouse

# --- a country to test against ---------------------------------------------

COUNTRY_ID = uuid.UUID("00000000-0000-0000-0000-0000000000c1")
COUNTRY_CODE = "BR"
NOMINAL_TEMP = Decimal("20.00")
TOLERANCE_TEMP = Decimal("5.00")
NOMINAL_HUMIDITY = Decimal("55.00")
TOLERANCE_HUMIDITY = Decimal("5.00")

FARM_ID = uuid.UUID("00000000-0000-0000-0000-0000000000f1")
FARM_REF = "BR-EXP-01"

WAREHOUSE_ID = uuid.UUID("00000000-0000-0000-0000-0000000000e1")
WAREHOUSE_REF = "BR-ENT-01"

SENSOR_ID = uuid.UUID("00000000-0000-0000-0000-0000000000a1")
SENSOR_CODE = "BR-SEN-01"

BATCH_ID = uuid.UUID("00000000-0000-0000-0000-0000000000b1")
BATCH_REF = "BR-2026-00042"
STORED_AT = date(2026, 7, 30)
QUALITY_GRADE = "A"
BATCH_STATUS = "stored"


def reference_country() -> list[Base]:
    """One country, one farm, one warehouse, one sensor, one batch in stock.

    The smallest world in which every join of the application resolves. A test
    that needs a second warehouse or an alert adds it to its own session.
    """
    return [
        Country(
            country_id=COUNTRY_ID,
            country_name="Brésil",
            country_code=COUNTRY_CODE,
            nominal_temp=NOMINAL_TEMP,
            tolerance_temp=TOLERANCE_TEMP,
            nominal_humidity=NOMINAL_HUMIDITY,
            tolerance_humidity=TOLERANCE_HUMIDITY,
        ),
        Farm(
            farm_id=FARM_ID,
            country_id=COUNTRY_ID,
            farm_name="Fazenda Santa Rita",
            farm_ref=FARM_REF,
        ),
        Warehouse(
            warehouse_id=WAREHOUSE_ID,
            country_id=COUNTRY_ID,
            warehouse_name="Entrepôt de Santos",
            warehouse_ref=WAREHOUSE_REF,
        ),
        Sensor(sensor_id=SENSOR_ID, warehouse_id=WAREHOUSE_ID, code=SENSOR_CODE),
        Batch(
            batch_id=BATCH_ID,
            warehouse_id=WAREHOUSE_ID,
            farm_id=FARM_ID,
            batch_ref=BATCH_REF,
            stored_at=STORED_AT,
            quality_grade=QUALITY_GRADE,
            batch_status=BATCH_STATUS,
        ),
    ]


@pytest_asyncio.fixture
async def empty_session() -> AsyncGenerator[AsyncSession, None]:
    """A session on empty tables, for a test that seeds its own world."""
    engine = create_async_engine(
        "sqlite+aiosqlite:///:memory:",
        poolclass=StaticPool,
        connect_args={"check_same_thread": False},
    )
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    factory = async_sessionmaker(engine, expire_on_commit=False)
    async with factory() as session:
        yield session

    await engine.dispose()


@pytest_asyncio.fixture
async def session(empty_session: AsyncSession) -> AsyncGenerator[AsyncSession, None]:
    """A session seeded with the reference country."""
    empty_session.add_all(reference_country())
    await empty_session.commit()
    yield empty_session


@pytest.fixture
def app(session: AsyncSession) -> FastAPI:
    """The real application, wired to the test session.

    A feature branch adds its router here rather than assembling a FastAPI of
    its own, so what the tests exercise is what `uvicorn app.main:app` serves.
    """
    application = create_app()
    application.dependency_overrides[get_session] = lambda: session
    return application


@pytest_asyncio.fixture
async def client(app: FastAPI) -> AsyncGenerator[AsyncClient, None]:
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as http_client:
        yield http_client
