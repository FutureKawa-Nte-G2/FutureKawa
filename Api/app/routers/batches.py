import uuid
from datetime import datetime
from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Query, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemas.batch import BatchCreate, BatchRead
from app.schemas.batch_history import BatchMeasurementHistory, Granularity
from app.security import require_api_key
from app.services.batch_history import (
    BatchNotFoundError,
    WindowTooWideError,
    batch_history,
)
from app.services.batches import (
    BatchAlreadyExistsError,
    FarmRefUnknownError,
    WarehouseRefUnknownError,
    create_batch,
)

router = APIRouter(prefix="/api", tags=["batches"])


@router.post(
    "/batches",
    response_model=BatchRead,
    status_code=status.HTTP_201_CREATED,
    dependencies=[Depends(require_api_key)],
)
async def post_batch(
    payload: BatchCreate,
    session: Annotated[AsyncSession, Depends(get_session)],
) -> BatchRead:
    """Register a batch described by an ERP reception file.

    Authenticated, unlike `GET /api/measurements`: this route writes to the
    database, and head office's reasons for leaving the read open — their client
    sends no header at all — do not apply to a caller we write ourselves.

    Rejections are split so the watcher can route the file without parsing
    prose: `409` means the file was already processed and can be archived as
    done, while `422` means the file is wrong and belongs in `error/`. Both
    carry their reason in `detail.code`.

    The full batch is returned, generated id included, so the watcher can log
    what it created without a second request.
    """
    try:
        batch = await create_batch(session, payload)
    except BatchAlreadyExistsError as exc:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail={"code": exc.code, "message": exc.message},
        ) from exc
    except (FarmRefUnknownError, WarehouseRefUnknownError) as exc:
        # 422 rather than 404: the URL exists, it is the payload that describes
        # something we do not know. Same status as a schema violation, which is
        # deliberate — both mean "this file cannot be processed", and the
        # watcher tells them apart on `detail.code`.
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
            detail={"code": exc.code, "message": exc.message},
        ) from exc

    return BatchRead.model_validate(batch)


@router.get("/batches/{batch_id}/measurements", response_model=BatchMeasurementHistory)
async def read_batch_history(
    batch_id: uuid.UUID,
    session: Annotated[AsyncSession, Depends(get_session)],
    granularity: Annotated[Granularity, Query()] = Granularity.DAILY,
    window_from: Annotated[datetime | None, Query(alias="from")] = None,
    window_to: Annotated[datetime | None, Query(alias="to")] = None,
) -> BatchMeasurementHistory:
    """Readings, alerts and the country band for one batch.

    Unauthenticated, like `GET /api/measurements`: the caller is head office's
    relay, whose client sends no header. Read-only and per-batch, so there is
    nothing here a warehouse could not already see on its own screen.

    `granularity=daily` returns one point per UTC day and is what a control
    chart over a storage period needs. `granularity=raw` returns the readings
    as the firmware sent them, over at most 7 days — a year of them would be
    ~105 000 points for a single curve. A raw call with no window gets the last
    7 days rather than a refusal.

    A batch received but not yet equipped with a sensor answers `200` with
    empty series and null bounds, never `404`: it exists, it simply has no
    history yet. `404` is reserved for a batch id that does not exist.
    """
    try:
        return await batch_history(
            session,
            batch_id=batch_id,
            granularity=granularity,
            window_from=window_from,
            window_to=window_to,
        )
    except BatchNotFoundError as exc:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail={"code": exc.code, "message": exc.message},
        ) from exc
    except WindowTooWideError as exc:
        raise HTTPException(
            status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
            detail={"code": exc.code, "message": exc.message},
        ) from exc
