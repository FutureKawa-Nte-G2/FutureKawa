import hmac
import os

from fastapi import Header, HTTPException, status

API_KEY_ENV_VAR = "LOCAL_API_KEY"


async def require_api_key(x_api_key: str | None = Header(default=None)) -> None:
    """Authenticate head office on the routes it pulls.

    The caller is a machine, not a person: there is no user to log in, no
    warehouse claim to read, and no session. A shared secret is the whole
    mechanism, carried in `X-API-Key` as agreed with the head office team.

    Refuses to serve when the secret is not configured, rather than serving
    unauthenticated. A route reachable from outside the country network must
    never fall back to open: an unset variable is a deployment mistake, and it
    should look like one.
    """
    expected = os.environ.get(API_KEY_ENV_VAR)
    if not expected:
        raise RuntimeError(
            f"{API_KEY_ENV_VAR} is not set. Configure it before serving the "
            "routes head office pulls."
        )

    # compare_digest rather than ==: key comparison should not leak its length
    # or its first differing byte through timing.
    if x_api_key is None or not hmac.compare_digest(x_api_key, expected):
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={"code": "invalid_api_key", "message": "Missing or invalid API key."},
        )
