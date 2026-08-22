"""Alembic environment for the country database.

The URL is not written in alembic.ini: it is read from `DATABASE_URL`, the same
variable the application uses. One connection string, one place to change it,
and no credentials committed.
"""

import asyncio
import os
import sys
from logging.config import fileConfig
from pathlib import Path

from sqlalchemy import pool
from sqlalchemy.engine import Connection
from sqlalchemy.ext.asyncio import async_engine_from_config

from alembic import context

# alembic runs from Api/, but not necessarily with it on the path.
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.db import DATABASE_URL_ENV_VAR  # noqa: E402
from app.models import Base  # noqa: E402

config = context.config

if config.config_file_name is not None:
    fileConfig(config.config_file_name)

# What autogenerate compares the database against.
target_metadata = Base.metadata


def _database_url() -> str:
    url = os.environ.get(DATABASE_URL_ENV_VAR)
    if not url:
        raise RuntimeError(
            f"{DATABASE_URL_ENV_VAR} is not set. Export it before running "
            "alembic, e.g. postgresql+asyncpg://futurekawa:futurekawa@"
            "localhost:5434/futurekawa_br"
        )
    return url


def run_migrations_offline() -> None:
    """Emit SQL to stdout instead of running it, for a DBA to review."""
    context.configure(
        url=_database_url(),
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )

    with context.begin_transaction():
        context.run_migrations()


def do_run_migrations(connection: Connection) -> None:
    context.configure(
        connection=connection,
        target_metadata=target_metadata,
        # Without this, a column changing from varchar(64) to varchar(128) is
        # invisible to autogenerate — and the MLD is full of explicit lengths.
        compare_type=True,
        compare_server_default=True,
    )

    with context.begin_transaction():
        context.run_migrations()


async def run_async_migrations() -> None:
    section = config.get_section(config.config_ini_section, {})
    section["sqlalchemy.url"] = _database_url()

    connectable = async_engine_from_config(
        section,
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )

    async with connectable.connect() as connection:
        await connection.run_sync(do_run_migrations)

    await connectable.dispose()


def run_migrations_online() -> None:
    asyncio.run(run_async_migrations())


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
