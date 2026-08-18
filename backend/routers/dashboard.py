from datetime import datetime, timedelta, timezone
from typing import Any

from fastapi import APIRouter, Depends
from sqlalchemy import func
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session as DbSession

from backend.config import DATABASE_CONFIG_ERROR
from backend.database import SessionLocal
from backend.dependencies.client_id import get_client_id

# Important: import Profile so SQLAlchemy can resolve Session.profile relationship
from backend.models.profile_model import Profile  # noqa: F401
from backend.models.session_model import Session, Recommendation


router = APIRouter(prefix="/api/dashboard", tags=["Dashboard"])


def normalize_action_category(action: str | None) -> str:
    if not action:
        return "Unknown"

    action_lower = action.lower()

    if "monitor" in action_lower:
        return "Monitor"

    if "maintain" in action_lower:
        return "Maintain"

    if "troubleshoot" in action_lower:
        return "Troubleshoot"

    if "escalate" in action_lower or "professional" in action_lower:
        return "Escalate"

    return action


def format_date(value: datetime | None) -> str | None:
    if not value:
        return None

    return value.strftime("%b %d, %Y")


def days_ago(value: datetime | None) -> int | None:
    if not value:
        return None

    if value.tzinfo is None:
        now = datetime.utcnow()
    else:
        now = datetime.now(value.tzinfo)

    return max((now - value).days, 0)


def empty_dashboard_response() -> dict[str, Any]:
    return {
        "server_time": datetime.now(timezone.utc).isoformat(),
        "totals": {
            "total_sessions": 0,
            "this_month_count": 0,
            "escalated_count": 0,
        },
        "last_diagnosis": None,
        "current_action_status": None,
        "recurring_issues_count": 0,
        "warning_signs_active_count": 0,
        "action_distribution": [
            {"label": "Monitor", "count": 0},
            {"label": "Maintain", "count": 0},
            {"label": "Troubleshoot", "count": 0},
            {"label": "Escalate", "count": 0},
        ],
        "session_frequency": [],
        "recent_warning_signs": [],
        "last_saved_session": None,
    }


def session_to_summary(session: Session | None) -> dict[str, Any] | None:
    if not session:
        return None

    return {
        "session_id": str(session.id),
        "symptom_type": session.symptom_type,
        "diagnosed_category": session.diagnosed_category,
        "action_category": normalize_action_category(session.action_category),
        "confidence_label": session.confidence_label,
        "created_at": session.created_at.isoformat() if session.created_at else None,
        "display_date": format_date(session.created_at),
        "days_ago": days_ago(session.created_at),
        "is_recurring": bool(session.is_recurring),
    }


@router.get("/summary")
def get_dashboard_summary(client_id: str = Depends(get_client_id)):
    """
    Dynamic dashboard data scoped to the requesting client.

    This endpoint reads saved diagnostic sessions and warning recommendations
    from the database. If the database has no records yet, it returns empty
    dashboard values instead of fake hardcoded values.
    """
    if SessionLocal is None:
        return {
            **empty_dashboard_response(),
            "database_warning": DATABASE_CONFIG_ERROR or "Database is not configured.",
        }

    db: DbSession = SessionLocal()

    try:
        now = datetime.now(timezone.utc)
        thirty_days_ago = now - timedelta(days=30)
        first_day_this_month = now.replace(day=1, hour=0, minute=0, second=0, microsecond=0)

        # All queries scoped to this client via Profile join
        client_sessions = (
            db.query(Session)
            .join(Profile, Session.profile_id == Profile.id)
            .filter(Profile.client_id == client_id)
        )

        total_sessions = client_sessions.count()

        this_month_count = (
            client_sessions
            .filter(Session.created_at >= first_day_this_month)
            .count()
        )

        escalated_count = (
            client_sessions
            .filter(Session.action_category.ilike("%escalate%"))
            .count()
        )

        last_session = (
            client_sessions
            .order_by(Session.created_at.desc())
            .first()
        )

        action_rows = (
            db.query(Session.action_category, func.count(Session.id))
            .join(Profile, Session.profile_id == Profile.id)
            .filter(Profile.client_id == client_id)
            .group_by(Session.action_category)
            .all()
        )

        action_counts = {
            "Monitor": 0,
            "Maintain": 0,
            "Troubleshoot": 0,
            "Escalate": 0,
        }

        for action_category, count in action_rows:
            normalized = normalize_action_category(action_category)

            if normalized in action_counts:
                action_counts[normalized] += count
            else:
                action_counts["Monitor"] += count

        recurring_rows = (
            db.query(Session.symptom_type, func.count(Session.id))
            .join(Profile, Session.profile_id == Profile.id)
            .filter(Profile.client_id == client_id)
            .group_by(Session.symptom_type)
            .having(func.count(Session.id) >= 2)
            .all()
        )

        recurring_issues_count = len(recurring_rows)

        warning_signs_active_count = (
            db.query(Recommendation)
            .join(Session, Recommendation.session_id == Session.id)
            .join(Profile, Session.profile_id == Profile.id)
            .filter(Profile.client_id == client_id)
            .count()
        )

        frequency_rows = (
            db.query(func.date(Session.created_at), func.count(Session.id))
            .join(Profile, Session.profile_id == Profile.id)
            .filter(Profile.client_id == client_id)
            .filter(Session.created_at >= thirty_days_ago)
            .group_by(func.date(Session.created_at))
            .order_by(func.date(Session.created_at))
            .all()
        )

        session_frequency = [
            {
                "date": str(row_date),
                "count": count,
            }
            for row_date, count in frequency_rows
        ]

        recent_warning_rows = (
            db.query(Recommendation)
            .join(Session, Recommendation.session_id == Session.id)
            .join(Profile, Session.profile_id == Profile.id)
            .filter(Profile.client_id == client_id)
            .order_by(Recommendation.created_at.desc())
            .limit(3)
            .all()
        )

        recent_warning_signs = [
            {
                "id": str(row.id),
                "warning_sign": row.warning_sign,
                "threshold": row.threshold,
                "recommended_action": row.recommended_action,
                "created_at": row.created_at.isoformat() if row.created_at else None,
                "display_date": format_date(row.created_at),
            }
            for row in recent_warning_rows
        ]

        last_summary = session_to_summary(last_session)

        return {
            "server_time": now.isoformat(),
            "totals": {
                "total_sessions": total_sessions,
                "this_month_count": this_month_count,
                "escalated_count": escalated_count,
            },
            "last_diagnosis": last_summary,
            "current_action_status": last_summary,
            "recurring_issues_count": recurring_issues_count,
            "warning_signs_active_count": warning_signs_active_count,
            "action_distribution": [
                {"label": "Monitor", "count": action_counts["Monitor"]},
                {"label": "Maintain", "count": action_counts["Maintain"]},
                {"label": "Troubleshoot", "count": action_counts["Troubleshoot"]},
                {"label": "Escalate", "count": action_counts["Escalate"]},
            ],
            "session_frequency": session_frequency,
            "recent_warning_signs": recent_warning_signs,
            "last_saved_session": last_summary,
        }

    except SQLAlchemyError as error:
        return {
            **empty_dashboard_response(),
            "database_warning": str(error),
        }
    finally:
        db.close()
