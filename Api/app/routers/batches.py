from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemas.batch import BatchCreate, BatchRead
from app.security import require_api_key
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
