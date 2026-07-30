from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.errors import BatchCreationError
from app.schemas.batch import BatchCreate, BatchRead
from app.security import CurrentUser, get_current_user
from app.services.batches import create_batch

router = APIRouter(prefix="/api/batches", tags=["batches"])


@router.post("", response_model=BatchRead, status_code=status.HTTP_201_CREATED)
async def post_batch(
    payload: BatchCreate,
    session: AsyncSession = Depends(get_session),
    current_user: CurrentUser = Depends(get_current_user),
) -> BatchRead:
    """Register a new batch in the warehouse the caller belongs to.

    Writes straight to the local country database: no call to head office is
    needed for this route.
    """
    try:
        batch = await create_batch(session, payload, current_user)
    except BatchCreationError as exc:
        raise HTTPException(
            status_code=422,
            detail={"code": exc.code, "message": exc.message},
        ) from exc

    return BatchRead.model_validate(batch)
