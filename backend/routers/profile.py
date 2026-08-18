from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import text
from sqlalchemy.orm import Session as DbSession
from sqlalchemy.exc import OperationalError, SQLAlchemyError
from uuid import UUID

from backend.database import get_db
from backend.dependencies.client_id import get_client_id
from backend.models.profile_model import Profile
from backend.schemas.profile_schema import ProfileCreate, ProfileResponse, ProfileUpdate

router = APIRouter(prefix="/api/profiles", tags=["Profiles"])
_storage_details_column_checked = False


def database_unavailable_error(error: Exception) -> HTTPException:
    return HTTPException(
        status_code=503,
        detail=(
            "Database is unavailable. Check backend/.env DATABASE_URL and confirm "
            f"the PostgreSQL/Supabase database is reachable. Details: {error}"
        ),
    )


def ensure_storage_details_column(db: DbSession) -> None:
    global _storage_details_column_checked

    if _storage_details_column_checked:
        return

    Profile.__table__.create(bind=db.get_bind(), checkfirst=True)
    db.execute(text("ALTER TABLE profiles ADD COLUMN IF NOT EXISTS storage_details JSONB"))
    db.commit()
    _storage_details_column_checked = True


@router.post("/save", response_model=ProfileResponse)
def save_profile(
    profile_create: ProfileCreate,
    client_id: str = Depends(get_client_id),
    db: DbSession = Depends(get_db),
):
    """
    Save a hardware profile to the database, scoped to the requesting client.
    If a profile with identical specifications already exists for this client,
    returns the existing profile.
    """
    try:
        ensure_storage_details_column(db)

        # Check if a profile with the same specifications already exists for this client
        existing_profile = (
            db.query(Profile)
            .filter(Profile.client_id == client_id)
            .filter(Profile.cpu_model == profile_create.cpu_model)
            .filter(Profile.ram_capacity == profile_create.ram_capacity)
            .filter(Profile.storage_type == profile_create.storage_type)
            .filter(Profile.storage_capacity == profile_create.storage_capacity)
            .filter(Profile.os_version == profile_create.os_version)
            .first()
        )

        if existing_profile:
            if profile_create.storage_details is not None:
                existing_profile.storage_details = profile_create.storage_details
                db.commit()
                db.refresh(existing_profile)

            return existing_profile

        # Create new profile tagged to this client
        new_profile = Profile(
            client_id=client_id,
            cpu_model=profile_create.cpu_model,
            ram_capacity=profile_create.ram_capacity,
            storage_type=profile_create.storage_type,
            storage_capacity=profile_create.storage_capacity,
            storage_details=profile_create.storage_details,
            os_version=profile_create.os_version,
            gpu_driver=profile_create.gpu_driver,
            chipset_driver=profile_create.chipset_driver,
            system_age=profile_create.system_age,
        )

        db.add(new_profile)
        db.commit()
        db.refresh(new_profile)

        return new_profile

    except OperationalError as error:
        db.rollback()
        raise database_unavailable_error(error)
    except SQLAlchemyError as error:
        db.rollback()
        raise HTTPException(
            status_code=500,
            detail=f"Failed to save profile: {error}",
        )
    except Exception as error:
        db.rollback()
        raise HTTPException(
            status_code=500,
            detail=f"Unexpected error while saving profile: {error}",
        )


@router.get("/{profile_id}", response_model=ProfileResponse)
def get_profile(
    profile_id: UUID,
    client_id: str = Depends(get_client_id),
    db: DbSession = Depends(get_db),
):
    """
    Retrieve a specific profile by ID, scoped to the requesting client.
    """
    try:
        ensure_storage_details_column(db)

        profile = (
            db.query(Profile)
            .filter(Profile.id == profile_id)
            .filter(Profile.client_id == client_id)
            .first()
        )

        if not profile:
            raise HTTPException(
                status_code=404,
                detail=f"Profile with ID {profile_id} not found",
            )

        return profile

    except OperationalError as error:
        raise database_unavailable_error(error)
    except SQLAlchemyError as error:
        raise HTTPException(
            status_code=500,
            detail=f"Failed to retrieve profile: {error}",
        )


@router.put("/{profile_id}", response_model=ProfileResponse)
def update_profile(
    profile_id: UUID,
    profile_update: ProfileUpdate,
    client_id: str = Depends(get_client_id),
    db: DbSession = Depends(get_db),
):
    """
    Update a saved hardware profile, scoped to the requesting client.
    Only fields included in the request body are changed.
    """
    try:
        ensure_storage_details_column(db)

        profile = (
            db.query(Profile)
            .filter(Profile.id == profile_id)
            .filter(Profile.client_id == client_id)
            .first()
        )

        if not profile:
            raise HTTPException(
                status_code=404,
                detail=f"Profile with ID {profile_id} not found",
            )

        update_data = profile_update.model_dump(exclude_unset=True)

        if not update_data:
            return profile

        for field, value in update_data.items():
            setattr(profile, field, value)

        db.commit()
        db.refresh(profile)

        return profile

    except HTTPException:
        raise
    except OperationalError as error:
        db.rollback()
        raise database_unavailable_error(error)
    except SQLAlchemyError as error:
        db.rollback()
        raise HTTPException(
            status_code=500,
            detail=f"Failed to update profile: {error}",
        )
    except Exception as error:
        db.rollback()
        raise HTTPException(
            status_code=500,
            detail=f"Unexpected error while updating profile: {error}",
        )


@router.get("", response_model=list[ProfileResponse])
def get_all_profiles(
    client_id: str = Depends(get_client_id),
    db: DbSession = Depends(get_db),
):
    """
    Retrieve all saved profiles for the requesting client.
    """
    try:
        ensure_storage_details_column(db)

        profiles = (
            db.query(Profile)
            .filter(Profile.client_id == client_id)
            .all()
        )
        return profiles

    except OperationalError as error:
        raise database_unavailable_error(error)
    except SQLAlchemyError as error:
        raise HTTPException(
            status_code=500,
            detail=f"Failed to retrieve profiles: {error}",
        )
