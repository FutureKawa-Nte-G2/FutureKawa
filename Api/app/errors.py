from fastapi import Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse


class BatchCreationError(Exception):
    """A batch could not be created. `code` is what the caller keys off."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


async def validation_error_handler(
    request: Request, exc: RequestValidationError
) -> JSONResponse:
    """Normalise Pydantic's 422 body into the same detail.{code, message} shape
    as every other rejection, so the caller always reads the reason from
    detail.code and never has to parse two formats.

    Must be registered on the app:
        app.add_exception_handler(RequestValidationError, validation_error_handler)
    """
    return JSONResponse(
        status_code=422,
        content={
            "detail": {
                "code": "schema_invalid",
                "message": "Payload does not match the expected batch schema.",
                "errors": jsonable_encoder(exc.errors()),
            }
        },
    )
