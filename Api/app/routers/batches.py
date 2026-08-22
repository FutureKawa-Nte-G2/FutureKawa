from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.errors import BatchCreationError
from app.schemas.batch import BatchCreate, BatchRead
from app.services.batches import create_batch

router = APIRouter(prefix="/api/batches", tags=["batches"])


@router.post("", response_model=BatchRead, status_code=status.HTTP_201_CREATED)
async def post_batch(
    payload: BatchCreate,
    session: AsyncSession = Depends(get_session),
) -> BatchRead:
    """Register a batch read from an ERP reception file.

    Internal route: the caller is the reception watcher running alongside this
    API, not a person and not head office. It carries no user identity, which is
    why nothing here reads a token — and why the route must not be exposed
    publicly as it stands.

    Writes straight to the local country database: no call to head office is
    needed for this route.
    """
    try:
        batch = await create_batch(session, payload)
    except BatchCreationError as exc:
        raise HTTPException(
            status_code=422,
            detail={"code": exc.code, "message": exc.message},
        ) from exc

    return BatchRead.model_validate(batch)
