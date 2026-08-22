from datetime import datetime

from fastapi import APIRouter, Depends
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemas.alert import AlertRead, AlertState
from app.security import require_api_key
from app.services.alerts import list_alerts

router = APIRouter(prefix="/api/alerts", tags=["alerts"])


@router.get("", response_model=list[AlertRead], dependencies=[Depends(require_api_key)])
async def get_alerts(
    since: datetime | None = None,
    state: AlertState | None = None,
    session: AsyncSession = Depends(get_session),
) -> list[AlertRead]:
    """List the country's alerts for head office, most recent first.

    Read-only, and deliberately not scoped to one warehouse: head office pulls
    the country in one call and dispatches on `warehouseRef`. Every row carries
    it, so nothing has to be inferred from the order of the response.

    An unknown `state` is rejected by the enum rather than silently returning
    everything, which would look like "no alerts match" and hide the typo.
    """
    rows = await list_alerts(session, since=since, state=state)
    return [AlertRead.model_validate(row) for row in rows]
