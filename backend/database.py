from sqlalchemy import create_engine
from fastapi import HTTPException
from sqlalchemy.orm import sessionmaker, declarative_base
from backend.config import DATABASE_CONFIG_ERROR, DATABASE_CONFIGURED, DATABASE_URL

engine = create_engine(DATABASE_URL) if DATABASE_CONFIGURED else None

SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine) if engine else None

Base = declarative_base()


def get_db():
    """
    FastAPI dependency — injects a DB session into any route that needs it.
    Automatically closes the session when the request is done.

    Usage in a router:
        @router.post("/something")
        def my_route(db: Session = Depends(get_db)):
            ...
    """
    if SessionLocal is None:
        raise HTTPException(
            status_code=503,
            detail=DATABASE_CONFIG_ERROR or "Database is not configured.",
        )

    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
