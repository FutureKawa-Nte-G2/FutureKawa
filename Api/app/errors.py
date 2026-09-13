"""Rejections shared by several routers.

A caller — head office's relay, a file watcher — reads the reason from
`detail.code` and never has to parse two formats. What lives here is what more
than one feature raises; anything specific to one service stays next to it.
"""

from fastapi import Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse


class WarehouseRefUnknownError(Exception):
    """No warehouse carries that reference.

    Shared rather than redefined per service: `/api/notifications` and
    `/api/alerts` both take `warehouse_ref` as their first argument and both
    answer `404` on an unknown one. Two classes with the same name and the same
    code would drift the day one of them changes its wording.
    """

    code = "warehouse_ref_unknown"

    def __init__(self, warehouse_ref: str) -> None:
        self.message = f"No warehouse found with reference {warehouse_ref}."
        super().__init__(self.message)


async def validation_error_handler(
    request: Request, exc: RequestValidationError
) -> JSONResponse:
    """Normalise Pydantic's 422 body into the same detail.{code, message} shape
    as every other rejection, so a caller always reads the reason from
    detail.code and never has to parse two formats.

    It matters most for the callers that are not human: a file watcher routing
    a reception file to `error/` needs one place to look for the reason.
    """
    return JSONResponse(
        status_code=422,
        content={
            "detail": {
                "code": "schema_invalid",
                "message": "Payload does not match the expected schema.",
                "errors": jsonable_encoder(exc.errors()),
            }
        },
    )
