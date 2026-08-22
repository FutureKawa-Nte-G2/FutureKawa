"""Database engine and session for the country database.

The engine is built on first use rather than at import time: the test suite
imports `get_session` only to override it, and must not need a database to do
so.
"""

import os
from collections.abc import AsyncGenerator

from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)

DATABASE_URL_ENV_VAR = "DATABASE_URL"

_engine: AsyncEngine | None = None
_session_factory: async_sessionmaker[AsyncSession] | None = None


def _database_url() -> str:
    url = os.environ.get(DATABASE_URL_ENV_VAR)
    if not url:
        raise RuntimeError(
            f"{DATABASE_URL_ENV_VAR} is not set. Expected an asyncpg URL, e.g. "
            "postgresql+asyncpg://user:password@localhost:5432/futurekawa_br"
        )
    return url


def get_engine() -> AsyncEngine:
    global _engine
    if _engine is None:
        # pool_pre_ping: the API sits idle between two head office pulls, long
        # enough for the database to have dropped the connection underneath it.
        _engine = create_async_engine(_database_url(), pool_pre_ping=True)
    return _engine


def get_session_factory() -> async_sessionmaker[AsyncSession]:
    global _session_factory
    if _session_factory is None:
        _session_factory = async_sessionmaker(get_engine(), expire_on_commit=False)
    return _session_factory


async def get_session() -> AsyncGenerator[AsyncSession, None]:
    """FastAPI dependency yielding one session per request.

    Rolls back whatever a failing request left open, so the connection returns
    to the pool clean rather than carrying a broken transaction to the next
    caller.
    """
    async with get_session_factory()() as session:
        try:
            yield session
        except Exception:
            await session.rollback()
            raise


async def dispose_engine() -> None:
    """Close the pool. Called on shutdown, and by tests that built an engine."""
    global _engine, _session_factory
    if _engine is not None:
        await _engine.dispose()
    _engine = None
    _session_factory = None
