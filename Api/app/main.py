"""Entry point of the country API.

One instance serves one country: it reads the local database, and head office
pulls it over HTTP. Nothing here talks to head office on its own initiative.

    uvicorn app.main:app --reload
"""

from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.exceptions import RequestValidationError

from app.db import dispose_engine
from app.errors import validation_error_handler
from app.routers.batches import router as batches_router
from app.routers.measurements import router as measurements_router
from app.routers.notifications import router as notifications_router


@asynccontextmanager
async def lifespan(app: FastAPI):
    yield
    # Close the pool on shutdown rather than leaving PostgreSQL to time the
    # connections out on its own.
    await dispose_engine()


def create_app() -> FastAPI:
    """Build the application.

    A factory rather than a module-level app so a test can build a second one
    with its own overrides without touching the first.

    Feature branches register their own router here — this is the one line each
    of them adds:

        from app.routers.batches import router as batches_router
        app.include_router(batches_router)
    app.include_router(notifications_router)
    """
    app = FastAPI(
        title="FutureKawa — API pays",
        description="API locale d'un pays : stock, capteurs, alertes.",
        version="0.1.0",
        lifespan=lifespan,
    )
    app.add_exception_handler(RequestValidationError, validation_error_handler)
    app.include_router(measurements_router)
    app.include_router(batches_router)
    app.include_router(notifications_router)

    @app.get("/health", tags=["ops"])
    async def health() -> dict[str, str]:
        """Liveness only: says the process answers, not that the database does.

        Deliberately does not touch the database — head office already treats a
        failed pull as a skipped cycle, and a health check that opens a
        connection every few seconds is a cost with no reader.
        """
        return {"status": "ok"}

    return app


app = create_app()
