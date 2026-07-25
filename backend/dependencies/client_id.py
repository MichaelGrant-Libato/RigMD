from fastapi import Header


def get_client_id(x_client_id: str = Header(default="legacy")) -> str:
    """
    FastAPI dependency that extracts the X-Client-ID header sent by every
    frontend request via fetchWithClient().

    Falls back to "legacy" so that pre-existing data (created before
    client-ID isolation was enforced) is still accessible under that key.
    """
    return x_client_id.strip() or "legacy"
