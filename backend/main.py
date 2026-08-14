from fastapi import FastAPI, Depends, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session as DbSession

from backend.config import FRONTEND_URL
from backend.database import get_db
from backend.models.profile_model import Profile
from backend.models.session_model import Session as DiagnosticSession
from backend.services.diagnostic_engine import run_diagnostic
from backend.services.resolution_service import check_resolution_for_diagnosis
from backend.routers import hardware, dashboard, history, recurring, warning_signs, profile
from backend.routers.remediation import router as remediation_router
from uuid import UUID

app = FastAPI(
    title="RigMD Backend",
    description="Backend API for RigMD diagnostic support system",
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        FRONTEND_URL,
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(hardware.router)
app.include_router(dashboard.router)
app.include_router(history.router)
app.include_router(recurring.router)
app.include_router(warning_signs.router)
app.include_router(remediation_router)
app.include_router(profile.router)


@app.get("/")
def read_root():
    return {"message": "Backend is running!"}


def clean_profile_value(value, fallback="Unknown"):
    if value is None or value == "":
        return fallback

    return str(value)


def get_or_create_live_profile(live_hardware_profile: dict, db: DbSession) -> Profile:
    cpu_model = clean_profile_value(
        live_hardware_profile.get("cpu", {}).get("name"),
        "Unknown CPU",
    )

    ram_total = live_hardware_profile.get("ram", {}).get("total_gb")
    ram_capacity = f"{ram_total} GB" if ram_total else "Unknown RAM"

    storage_type = clean_profile_value(
        live_hardware_profile.get("storage_type"),
        "Unknown Storage",
    )

    disk_total = live_hardware_profile.get("disk", {}).get("total_gb")
    storage_capacity = f"{disk_total} GB" if disk_total else "Unknown Capacity"

    os_version = clean_profile_value(
        live_hardware_profile.get("os_version"),
        "Unknown OS",
    )

    gpu_driver = clean_profile_value(
        live_hardware_profile.get("gpu", {}).get("driver"),
        "Unknown GPU Driver",
    )

    chipset_driver = clean_profile_value(
        live_hardware_profile.get("chipset_driver"),
        "Unknown Chipset",
    )

    system_age = clean_profile_value(
        live_hardware_profile.get("system_age"),
        "Unknown",
    )

    existing_profile = (
        db.query(Profile)
        .filter(Profile.cpu_model == cpu_model)
        .filter(Profile.ram_capacity == ram_capacity)
        .filter(Profile.storage_type == storage_type)
        .filter(Profile.storage_capacity == storage_capacity)
        .filter(Profile.os_version == os_version)
        .first()
    )

    if existing_profile:
        return existing_profile

    profile = Profile(
        cpu_model=cpu_model,
        ram_capacity=ram_capacity,
        storage_type=storage_type,
        storage_capacity=storage_capacity,
        os_version=os_version,
        gpu_driver=gpu_driver,
        chipset_driver=chipset_driver,
        system_age=system_age,
    )

    db.add(profile)
    db.flush()

    return profile


def apply_resolution_fields(session: DiagnosticSession, values: dict) -> None:
    for key, value in values.items():
        if hasattr(session, key):
            setattr(session, key, value)


def save_diagnostic_session(
    payload: dict,
    result: dict,
    live_hardware_profile: dict,
    db: DbSession,
) -> dict:
    profile = get_or_create_live_profile(live_hardware_profile, db)

    session = DiagnosticSession(
        profile_id=profile.id,
        symptom_type=result.get("symptom_type") or payload.get("symptom_type", ""),
        affected_activity=result.get("affected_activity") or payload.get("affected_activity", ""),
        frequency=result.get("frequency") or payload.get("frequency", ""),
        severity=result.get("severity") or payload.get("severity", ""),
        duration=payload.get("duration", "N/A"),
        recent_changes=result.get("recent_changes") or payload.get("recent_changes", ""),
        system_state=payload.get("system_state", ""),
        warning_signs=result.get("warning_signs") or payload.get("warning_signs", ""),
        diagnosed_category=result.get("diagnosed_category", ""),
        action_category=result.get("action_category", ""),
        confidence_label=result.get("confidence_label", ""),
        ai_explanation=result.get("ai_explanation", ""),
        is_recurring=False,
    )

    apply_resolution_fields(
        session,
        {
            "resolution_status": result.get("resolution_status", "open"),
            "resolution_checked_at": result.get("resolution_checked_at"),
            "resolution_summary": result.get("resolution_summary", ""),
            "resolution_proof": result.get("resolution_proof", []),
            "last_action_status": result.get("last_action_status"),
            "last_action_summary": result.get("last_action_summary", ""),
        },
    )

    db.add(session)
    db.commit()
    db.refresh(session)

    result["session_id"] = str(session.id)
    result["profile_id"] = str(profile.id)
    result["created_at"] = session.created_at.isoformat() if session.created_at else result.get("created_at")

    result.setdefault("resolution_status", getattr(session, "resolution_status", "open"))
    result.setdefault("resolution_checked_at", getattr(session, "resolution_checked_at", None))
    result.setdefault("resolution_summary", getattr(session, "resolution_summary", ""))
    result.setdefault("resolution_proof", getattr(session, "resolution_proof", []))
    result.setdefault("last_action_status", getattr(session, "last_action_status", None))
    result.setdefault("last_action_summary", getattr(session, "last_action_summary", ""))

    return result


@app.post("/api/diagnosis/submit")
def run_diagnostic_endpoint(payload: dict, db: DbSession = Depends(get_db)):
    try:
        live_hardware_profile = hardware.get_live_hardware_stats()
    except Exception:
        live_hardware_profile = {}

    result = run_diagnostic(symptom_data=payload, profile_data=live_hardware_profile)

    try:
        return save_diagnostic_session(
            payload=payload,
            result=result,
            live_hardware_profile=live_hardware_profile,
            db=db,
        )
    except SQLAlchemyError as error:
        db.rollback()
        raise HTTPException(
            status_code=500,
            detail=f"Diagnosis completed but failed to save session: {error}",
        )


@app.post("/api/diagnosis/{session_id}/needs-recheck")
def mark_diagnosis_needs_recheck(session_id: UUID, db: DbSession = Depends(get_db)):
    session = (
        db.query(DiagnosticSession)
        .filter(DiagnosticSession.id == session_id)
        .first()
    )

    if not session:
        raise HTTPException(status_code=404, detail="Diagnosis record not found")

    result = {
        "resolution_status": "needs_recheck",
        "last_action_status": "completed",
        "last_action_summary": "A safe action was performed. Run a follow-up check to see if the issue improved.",
    }

    apply_resolution_fields(session, result)

    db.commit()

    return {
        "session_id": str(session.id),
        **result,
    }


@app.post("/api/diagnosis/{session_id}/check-resolution")
def check_diagnosis_resolution(session_id: UUID, db: DbSession = Depends(get_db)):
    session = (
        db.query(DiagnosticSession)
        .filter(DiagnosticSession.id == session_id)
        .first()
    )

    if not session:
        raise HTTPException(status_code=404, detail="Diagnosis record not found")

    try:
        live_hardware_profile = hardware.get_live_hardware_stats()
    except Exception:
        live_hardware_profile = {}

    diagnosis_record = {
        "session_id": str(session.id),
        "diagnosed_category": session.diagnosed_category,
        "action_category": session.action_category,
        "confidence_label": session.confidence_label,
    }

    result = check_resolution_for_diagnosis(
        diagnosis_record=diagnosis_record,
        profile_data=live_hardware_profile,
    )

    apply_resolution_fields(session, result)

    db.commit()

    return {
        "session_id": str(session.id),
        **result,
    }