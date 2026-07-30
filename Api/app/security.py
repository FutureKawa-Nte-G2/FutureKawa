from dataclasses import dataclass


@dataclass(frozen=True)
class CurrentUser:
    """The logged-in user, as read from the JWT. `warehouse_id` and `id` are the
    only claims this endpoint needs.
    """

    id: int
    warehouse_id: int


async def get_current_user() -> CurrentUser:
    """Resolve the caller from the Authorization header.

    Left unimplemented on purpose: JWT decoding belongs to the auth work, which
    owns the signing key and the claim names. Whoever wires it must return a
    CurrentUser built from the token's `sub` (user id) and `warehouse_id`
    claims, and reject the request when either claim is missing.

    Raising rather than returning a placeholder is deliberate: a wrong
    warehouse_id would silently file batches into the wrong warehouse.
    """
    raise NotImplementedError(
        "JWT decoding is not wired yet. Provide get_current_user before serving "
        "POST /api/batches."
    )
