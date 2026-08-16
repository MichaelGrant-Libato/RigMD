"""
Dependency: extract the X-Client-ID header from an incoming request.

Every endpoint that reads or writes client-specific diagnostic data must
declare this dependency so that queries are scoped to the correct client.

Usage inside a router:
    from backend.dependencies.client_id import get_client_id

    @router.get("/sessions")
    def get_sessions(
        client_id: str = Depends(get_client_id),
        db: DbSession = Depends(get_db),
    ):
        ...
"""

from fastapi import Header, HTTPException


def get_client_id(x_client_id: str = Header(default="")) -> str:
    """
    Read the X-Client-ID request header.

    Raises HTTP 400 if the header is missing or blank so that every
    data-reading endpoint is explicitly tied to a client identity.
    """
    client_id = (x_client_id or "").strip()

    if not client_id:
        raise HTTPException(
            status_code=400,
            detail=(
                "X-Client-ID header is required. "
                "Each client must send a stable, unique identifier with every request."
            ),
        )

    return client_id
