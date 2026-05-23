from collections import Counter, defaultdict
from datetime import datetime, timezone
from typing import Any
from uuid import UUID

from fastapi import APIRouter, Depends
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session as DbSession, joinedload

from backend.database import get_db
from backend.models.session_model import Session as DiagnosticSession


router = APIRouter(prefix="/api/recurring", tags=["Recurring Patterns"])


ACTION_RANK = {
    "Monitor": 1,
    "Maintain": 2,
    "Troubleshoot": 3,
    "Escalate": 4,
}


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


def format_short_date(value: datetime | None) -> str:
    if not value:
        return "N/A"

    return value.strftime("%b %d")


def format_time(value: datetime | None) -> str:
    if not value:
        return "No time"

    return value.strftime("%I:%M %p")


def split_warning_signs(raw_warning_signs: str | None) -> list[str]:
    if not raw_warning_signs:
        return []

    cleaned = raw_warning_signs.replace(";", "\n").replace(",", "\n")
    return [item.strip() for item in cleaned.splitlines() if item.strip()]


def get_action_rank(action: str | None) -> int:
    return ACTION_RANK.get(normalize_action_category(action), 1)


def get_pattern_status(previous_action: str, updated_action: str) -> str:
    previous_rank = get_action_rank(previous_action)
    updated_rank = get_action_rank(updated_action)

    if updated_rank > previous_rank:
        return "Worsening"

    if updated_rank < previous_rank:
        return "Improving"

    return "Stable"


def get_recommended_next_step(status: str, updated_action: str, probable_cause: str) -> str:
    action = normalize_action_category(updated_action)

    if status == "Worsening" or action == "Escalate":
        return (
            f"The repeated pattern related to {probable_cause} is getting worse. "
            "Prepare a diagnostic report and consider professional inspection if the issue continues."
        )

    if action == "Troubleshoot":
        return (
            f"Start troubleshooting steps for {probable_cause}. "
            "Review recent driver, storage, startup, or software changes connected to this symptom."
        )

    if action == "Maintain":
        return (
            f"Continue maintenance actions related to {probable_cause}. "
            "Monitor if the same symptom appears again in the next diagnostic session."
        )

    return (
        f"Keep monitoring this pattern related to {probable_cause}. "
        "Run another diagnostic if the symptom becomes more frequent or severe."
    )


def session_to_occurrence(session: DiagnosticSession, index: int, status: str) -> dict[str, Any]:
    action = normalize_action_category(session.action_category)
    confidence = normalize_confidence_label(session.confidence_label)

    return {
        "id": str(session.id),
        "session_code": f"S-{index + 1:03d}",
        "display_date": format_date(session.created_at),
        "short_date": format_short_date(session.created_at),
        "display_time": format_time(session.created_at),
        "symptom": session.symptom_type,
        "probable_cause": session.diagnosed_category,
        "action_category": action,
        "confidence_label": confidence,
        "severity": session.severity,
        "frequency": session.frequency,
        "duration": session.duration or "N/A",
        "warning_signs": split_warning_signs(session.warning_signs),
        "status": status,
    }


def create_pattern(pattern_key: str, sessions: list[DiagnosticSession], index: int) -> dict[str, Any] | None:
    if len(sessions) < 2:
        return None

    sorted_sessions = sorted(
        sessions,
        key=lambda item: item.created_at or datetime.min.replace(tzinfo=timezone.utc),
    )

    first_session = sorted_sessions[0]
    latest_session = sorted_sessions[-1]

    symptom_counter = Counter([item.symptom_type for item in sorted_sessions if item.symptom_type])
    cause_counter = Counter([item.diagnosed_category for item in sorted_sessions if item.diagnosed_category])

    symptom_name = symptom_counter.most_common(1)[0][0] if symptom_counter else "Repeated Symptom"
    probable_cause = cause_counter.most_common(1)[0][0] if cause_counter else "Unspecified Cause"

    previous_action = normalize_action_category(first_session.action_category)
    updated_action = normalize_action_category(latest_session.action_category)

    status = get_pattern_status(previous_action, updated_action)
    action_escalated = get_action_rank(updated_action) > get_action_rank(previous_action)

    occurrences = [
        session_to_occurrence(session=session, index=occurrence_index, status=status)
        for occurrence_index, session in enumerate(sorted_sessions)
    ]

    return {
        "id": f"RP-{index + 1:03d}",
        "pattern_key": pattern_key,
        "symptom": symptom_name,
        "probable_cause": probable_cause,
        "occurrence_count": len(sorted_sessions),
        "first_detected": format_date(first_session.created_at),
        "latest_detected": format_date(latest_session.created_at),
        "previous_action": previous_action,
        "updated_action": updated_action,
        "status": status,
        "action_escalated": action_escalated,
        "recommended_next_step": get_recommended_next_step(status, updated_action, probable_cause),
        "occurrences": occurrences,
        "timeline": [
            {
                "date": occurrence["short_date"],
                "full_date": occurrence["display_date"],
                "session_id": occurrence["id"],
            }
            for occurrence in occurrences
        ],
    }


def empty_response(database_warning: str | None = None) -> dict[str, Any]:
    response = {
        "metrics": {
            "recurring_issues": 0,
            "worsening_trends": 0,
            "action_escalated": 0,
            "total_occurrences": 0,
        },
        "patterns": [],
        "timeline": [],
    }

    if database_warning:
        response["database_warning"] = database_warning

    return response


@router.get("/patterns")
def get_recurring_patterns(db: DbSession = Depends(get_db)):
    try:
        sessions = (
            db.query(DiagnosticSession)
            .options(joinedload(DiagnosticSession.recommendations))
            .order_by(DiagnosticSession.created_at.asc())
            .all()
        )

        if not sessions:
            return empty_response()

        symptom_groups: dict[str, list[DiagnosticSession]] = defaultdict(list)
        cause_groups: dict[str, list[DiagnosticSession]] = defaultdict(list)

        for session in sessions:
            if session.symptom_type:
                symptom_groups[session.symptom_type].append(session)

            if session.diagnosed_category:
                cause_groups[session.diagnosed_category].append(session)

        patterns = []
        used_display_pairs = set()

        for symptom, grouped_sessions in symptom_groups.items():
            if len(grouped_sessions) < 2:
                continue

            cause_counter = Counter(
                [item.diagnosed_category for item in grouped_sessions if item.diagnosed_category]
            )
            primary_cause = cause_counter.most_common(1)[0][0] if cause_counter else "Unspecified Cause"
            display_pair = f"{symptom}|{primary_cause}"

            pattern = create_pattern(
                pattern_key=f"symptom:{symptom}",
                sessions=grouped_sessions,
                index=len(patterns),
            )

            if pattern:
                patterns.append(pattern)
                used_display_pairs.add(display_pair)

        for cause, grouped_sessions in cause_groups.items():
            if len(grouped_sessions) < 2:
                continue

            symptom_counter = Counter(
                [item.symptom_type for item in grouped_sessions if item.symptom_type]
            )
            primary_symptom = symptom_counter.most_common(1)[0][0] if symptom_counter else "Repeated Symptom"
            display_pair = f"{primary_symptom}|{cause}"

            if display_pair in used_display_pairs:
                continue

            pattern = create_pattern(
                pattern_key=f"cause:{cause}",
                sessions=grouped_sessions,
                index=len(patterns),
            )

            if pattern:
                patterns.append(pattern)
                used_display_pairs.add(display_pair)

        patterns = sorted(
            patterns,
            key=lambda item: item["occurrence_count"],
            reverse=True,
        )

        timeline = []

        for pattern in patterns:
            for occurrence in pattern["occurrences"]:
                timeline.append(
                    {
                        "pattern_id": pattern["id"],
                        "date": occurrence["display_date"],
                        "symptom": occurrence["symptom"],
                        "probable_cause": occurrence["probable_cause"],
                        "action_category": occurrence["action_category"],
                        "confidence_label": occurrence["confidence_label"],
                        "status": pattern["status"],
                    }
                )

        metrics = {
            "recurring_issues": len(patterns),
            "worsening_trends": len([item for item in patterns if item["status"] == "Worsening"]),
            "action_escalated": len([item for item in patterns if item["action_escalated"]]),
            "total_occurrences": sum(item["occurrence_count"] for item in patterns),
        }

        return {
            "metrics": metrics,
            "patterns": patterns,
            "timeline": timeline,
        }

    except SQLAlchemyError as error:
        return empty_response(database_warning=str(error))


@router.get("/patterns/{pattern_id}")
def get_recurring_pattern_detail(pattern_id: str, db: DbSession = Depends(get_db)):
    response = get_recurring_patterns(db)

    for pattern in response.get("patterns", []):
        if pattern["id"] == pattern_id:
            return {"pattern": pattern}

    return {"pattern": None}