"""Shared-secret authentication for the routes that write to the country
database.

The caller is a machine, not a person: a reception file watcher has no session,
no user and no warehouse claim to read. A shared secret carried in `X-API-Key`
is the whole mechanism.

Read at request time rather than at import time, like the engine in `app.db`:
the test suite must be able to import the application without a secret in the
environment.
"""

import hmac
import os

from fastapi import Header, HTTPException, status

API_KEY_ENV_VAR = "LOCAL_API_KEY"


async def require_api_key(x_api_key: str | None = Header(default=None)) -> None:
    """Reject a caller that does not carry the agreed secret.

    Refuses to serve when the secret is not configured, rather than serving
    unauthenticated. A route that writes to the database must never fall back to
    open: an unset variable is a deployment mistake, and it should look like
    one.
    """
    expected = os.environ.get(API_KEY_ENV_VAR)
    if not expected:
        raise RuntimeError(
            f"{API_KEY_ENV_VAR} is not set. Configure it before serving the "
            "routes that write to the database."
        )

    # compare_digest rather than ==: comparing a secret should not leak its
    # length or its first differing byte through timing. Encoded to bytes
    # because the str form raises TypeError on any non-ASCII character, which
    # would turn a hostile header into a 500 instead of a 401.
    presented = (x_api_key or "").encode("utf-8")
    if not x_api_key or not hmac.compare_digest(presented, expected.encode("utf-8")):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={
                "code": "invalid_api_key",
                "message": "Missing or invalid API key.",
            },
        )
