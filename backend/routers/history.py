from datetime import datetime, timezone
from typing import Any
from uuid import UUID

from fastapi import APIRouter, Depends, Query
from sqlalchemy import func, or_
from sqlalchemy.orm import Session as DbSession, joinedload
from sqlalchemy.exc import OperationalError

from backend.database import get_db
from backend.models.session_model import Session as DiagnosticSession

router = APIRouter(prefix="/api/diagnosis", tags=["Diagnostic History"])


def normalize_action_category(action: str | None) -> str:
    if not action:
        return "Monitor"

    value = action.lower()

    if "escalate" in value or "professional" in value:
        return "Escalate"
    if "troubleshoot" in value:
        return "Troubleshoot"
    if "maintain" in value:
        return "Maintain"
    if "monitor" in value:
        return "Monitor"

    return action


def normalize_confidence_label(confidence: str | None) -> str:
    if not confidence:
        return "Low Confidence"

    value = confidence.lower()

    if "high" in value:
        return "High Confidence"
    if "moderate" in value or "medium" in value:
        return "Moderate"
    if "low" in value:
        return "Low Confidence"

    return confidence


def format_date(value: datetime | None) -> str:
    if not value:
        return "No date"
    return value.strftime("%b %d, %Y")


def format_time(value: datetime | None) -> str:
    if not value:
        return "No time"
    return value.strftime("%I:%M %p")


def split_warning_signs(raw_warning_signs: str | None) -> list[str]:
    if not raw_warning_signs:
        return []
    cleaned = raw_warning_signs.replace(";", "\n").replace(",", "\n")
    return [item.strip() for item in cleaned.splitlines() if item.strip()]


def get_recommended_next_step(action_category: str, probable_cause: str) -> str:
    action = normalize_action_category(action_category)

    if action == "Escalate":
        return "Prepare a diagnostic report and consult a qualified technician for professional inspection."
    if action == "Troubleshoot":
        return f"Start troubleshooting steps related to {probable_cause}. Review recent software or driver changes."
    if action == "Maintain":
        return f"Perform maintenance steps related to {probable_cause}. Check cleanup, updates, and basic system health."

    return f"Monitor the issue related to {probable_cause}. Run another diagnostic session if symptoms continue."


def build_recurring_lookup(db: DbSession) -> set[str]:
    rows = (
        db.query(DiagnosticSession.symptom_type, func.count(DiagnosticSession.id))
        .group_by(DiagnosticSession.symptom_type)
        .having(func.count(DiagnosticSession.id) >= 2)
        .all()
    )
    return {row[0] for row in rows if row[0]}


def session_to_dict(
    session: DiagnosticSession,
    recurring_symptoms: set[str],
    index: int,
) -> dict[str, Any]:
    action = normalize_action_category(session.action_category)
    confidence = normalize_confidence_label(session.confidence_label)
    warning_signs = split_warning_signs(session.warning_signs)

    recommendations = []
    for recommendation in session.recommendations:
        recommendations.append(
            {
                "warning_sign": recommendation.warning_sign,
                "threshold": recommendation.threshold,
                "recommended_action": recommendation.recommended_action,
            }
        )
        if recommendation.warning_sign and recommendation.warning_sign not in warning_signs:
            warning_signs.append(recommendation.warning_sign)

    recommended_next_step = (
        recommendations[0]["recommended_action"]
        if recommendations
        else get_recommended_next_step(action, session.diagnosed_category)
    )

    is_recurring = bool(session.is_recurring) or session.symptom_type in recurring_symptoms

    return {
        "session_id": str(session.id),
        "session_code": f"S-{index + 1:03d}",
        "display_date": format_date(session.created_at),
        "display_time": format_time(session.created_at),
        "duration": session.duration or "N/A",
        "created_at": session.created_at.isoformat() if session.created_at else None,
        "symptom_type": session.symptom_type,
        "affected_activity": session.affected_activity,
        "frequency": session.frequency,
        "severity": session.severity,
        "recent_changes": session.recent_changes,
        "system_state": session.system_state,
        "diagnosed_category": session.diagnosed_category,
        "action_category": action,
        "confidence_label": confidence,
        "ai_explanation": session.ai_explanation,
        "is_recurring": is_recurring,
        "warning_signs": warning_signs,
        "recommendations": recommendations,
        "recommended_next_step": recommended_next_step,
    }


def build_metrics(db: DbSession) -> dict[str, int]:
    now = datetime.now(timezone.utc)
    month_start = now.replace(day=1, hour=0, minute=0, second=0, microsecond=0)

    total_sessions = db.query(DiagnosticSession).count()

    recurring_rows = (
        db.query(DiagnosticSession.symptom_type, func.count(DiagnosticSession.id))
        .group_by(DiagnosticSession.symptom_type)
        .having(func.count(DiagnosticSession.id) >= 2)
        .all()
    )
    recurring_issues = len(recurring_rows)

    escalated = (
        db.query(DiagnosticSession)
        .filter(DiagnosticSession.action_category.ilike("%escalate%"))
        .count()
    )

    this_month = (
        db.query(DiagnosticSession)
        .filter(DiagnosticSession.created_at >= month_start)
        .count()
    )

    return {
        "total_sessions": total_sessions,
        "recurring_issues": recurring_issues,
        "escalated": escalated,
        "this_month": this_month,
    }


@router.get("/sessions")
def get_history_sessions(
    search: str = Query(default=""),
    action: str = Query(default="all"),
    recurring_only: bool = Query(default=False),
    sort: str = Query(default="newest"),
    db: DbSession = Depends(get_db),
):
    try:
        recurring_symptoms = build_recurring_lookup(db)
        query = db.query(DiagnosticSession).options(joinedload(DiagnosticSession.recommendations))

        if search.strip():
            keyword = f"%{search.strip()}%"
            query = query.filter(
                or_(
                    DiagnosticSession.symptom_type.ilike(keyword),
                    DiagnosticSession.diagnosed_category.ilike(keyword),
                    DiagnosticSession.action_category.ilike(keyword),
                    DiagnosticSession.confidence_label.ilike(keyword),
                    DiagnosticSession.warning_signs.ilike(keyword),
                )
            )

        action_value = action.lower().strip()
        if action_value != "all":
            if action_value == "escalate":
                query = query.filter(DiagnosticSession.action_category.ilike("%escalate%"))
            elif action_value == "troubleshoot":
                query = query.filter(DiagnosticSession.action_category.ilike("%troubleshoot%"))
            elif action_value == "maintain":
                query = query.filter(DiagnosticSession.action_category.ilike("%maintain%"))
            elif action_value == "monitor":
                query = query.filter(DiagnosticSession.action_category.ilike("%monitor%"))

        if sort == "oldest":
            query = query.order_by(DiagnosticSession.created_at.asc())
        else:
            query = query.order_by(DiagnosticSession.created_at.desc())

        sessions = query.all()

        if recurring_only:
            sessions = [
                session
                for session in sessions
                if bool(session.is_recurring) or session.symptom_type in recurring_symptoms
            ]

        items = [
            session_to_dict(session=session, recurring_symptoms=recurring_symptoms, index=index)
            for index, session in enumerate(sessions)
        ]

        return {
            "metrics": build_metrics(db),
            "sessions": items,
        }

    except OperationalError:
        print("\n[WARNING] Database connection refused. Ensure PostgreSQL is running on port 5432.\n")
        return {
            "metrics": {"total_sessions": 0, "recurring_issues": 0, "escalated": 0, "this_month": 0},
            "sessions": [],
            "database_error": True
        }


@router.get("/sessions/{session_id}")
def get_history_session_detail(
    session_id: UUID,
    db: DbSession = Depends(get_db),
):
    try:
        recurring_symptoms = build_recurring_lookup(db)
        session = (
            db.query(DiagnosticSession)
            .options(joinedload(DiagnosticSession.recommendations))
            .filter(DiagnosticSession.id == session_id)
            .first()
        )

        if not session:
            return {
                "message": "Session not found",
                "session": None,
            }

        return {
            "session": session_to_dict(
                session=session,
                recurring_symptoms=recurring_symptoms,
                index=0,
            )
        }
    except OperationalError:
        print("\n[WARNING] Database connection refused. Ensure PostgreSQL is running on port 5432.\n")
        return {
            "message": "Database connection error.",
            "session": None
        }