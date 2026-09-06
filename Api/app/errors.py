from fastapi import Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse


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
