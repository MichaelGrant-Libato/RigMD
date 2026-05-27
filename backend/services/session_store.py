from datetime import datetime
from collections import defaultdict


_sessions = []


def _parse_date(value):
    try:
        return datetime.fromisoformat(str(value))
    except Exception:
        return datetime.now()


def _display_date(value):
    return _parse_date(value).strftime("%b %d, %Y")


def _short_date(value):
    return _parse_date(value).strftime("%m/%d")


def _display_time(value):
    return _parse_date(value).strftime("%I:%M %p")


def _days_ago(value):
    return max((datetime.now() - _parse_date(value)).days, 0)


def _severity_rank(value):
    value = str(value or "").lower()

    if "high" in value:
        return 3
    if "medium" in value or "moderate" in value:
        return 2
    if "low" in value:
        return 1

    return 0


def _pattern_key(session):
    symptom = str(session.get("symptom_type", "")).strip().lower()
    cause = str(session.get("diagnosed_category", "")).strip().lower()
    return f"{symptom}::{cause}"


def _session_summary(session):
    created_at = session.get("created_at")

    return {
        "session_id": session.get("session_id"),
        "session_code": session.get("session_code"),
        "symptom_type": session.get("symptom_type", ""),
        "affected_activity": session.get("affected_activity", ""),
        "frequency": session.get("frequency", ""),
        "severity": session.get("severity", ""),
        "warning_signs": session.get("warning_signs", ""),
        "recent_changes": session.get("recent_changes", ""),
        "diagnosed_category": session.get("diagnosed_category", ""),
        "action_category": session.get("action_category", ""),
        "confidence_label": session.get("confidence_label", ""),
        "created_at": created_at,
        "display_date": _display_date(created_at),
        "days_ago": _days_ago(created_at),
        "is_recurring": session.get("is_recurring", False),
    }


def save_session(result):
    session = dict(result)

    if not session.get("created_at"):
        session["created_at"] = datetime.now().isoformat()

    session["session_code"] = f"RIG-{len(_sessions) + 1:04d}"
    session["is_recurring"] = False

    _sessions.append(session)

    grouped = defaultdict(list)

    for item in _sessions:
        grouped[_pattern_key(item)].append(item)

    for group in grouped.values():
        is_recurring = len(group) >= 2

        for item in group:
            item["is_recurring"] = is_recurring

    return session


def list_sessions(search="", action="all", recurring_only=False, sort="newest"):
    filtered = list(_sessions)

    search_text = str(search or "").lower().strip()
    action_text = str(action or "all").lower().strip()

    if search_text:
        filtered = [
            session for session in filtered
            if search_text in str(session.get("symptom_type", "")).lower()
            or search_text in str(session.get("diagnosed_category", "")).lower()
            or search_text in str(session.get("action_category", "")).lower()
        ]

    if action_text and action_text != "all":
        filtered = [
            session for session in filtered
            if action_text in str(session.get("action_category", "")).lower()
        ]

    if recurring_only:
        filtered = [session for session in filtered if session.get("is_recurring")]

    reverse = sort != "oldest"
    filtered.sort(key=lambda item: _parse_date(item.get("created_at")), reverse=reverse)

    now = datetime.now()

    return {
        "metrics": {
            "total_sessions": len(_sessions),
            "recurring_issues": sum(1 for session in _sessions if session.get("is_recurring")),
            "escalated": sum(
                1 for session in _sessions
                if "escalate" in str(session.get("action_category", "")).lower()
            ),
            "this_month": sum(
                1 for session in _sessions
                if _parse_date(session.get("created_at")).month == now.month
                and _parse_date(session.get("created_at")).year == now.year
            ),
        },
        "sessions": [_session_summary(session) for session in filtered],
    }


def get_session(session_id):
    for session in _sessions:
        if session.get("session_id") == session_id:
            return session

    return None


def get_recurring_patterns():
    grouped = defaultdict(list)

    for session in _sessions:
        grouped[_pattern_key(session)].append(session)

    patterns = []
    timeline_rows = []

    for key, group in grouped.items():
        if len(group) < 2:
            continue

        group.sort(key=lambda item: _parse_date(item.get("created_at")))

        first = group[0]
        latest = group[-1]

        previous_action = first.get("action_category", "Monitor")
        updated_action = latest.get("action_category", previous_action)

        worsening = _severity_rank(latest.get("severity")) > _severity_rank(first.get("severity"))

        if worsening and "escalate" not in updated_action.lower():
            updated_action = "Troubleshoot"

        action_escalated = previous_action != updated_action
        status = "Worsening" if worsening else "Recurring"

        pattern_id = key.replace("::", "-").replace(" ", "-")

        occurrences = []

        for session in group:
            created_at = session.get("created_at")

            occurrence = {
                "id": session.get("session_id"),
                "session_code": session.get("session_code", ""),
                "display_date": _display_date(created_at),
                "short_date": _short_date(created_at),
                "display_time": _display_time(created_at),
                "symptom": session.get("symptom_type", ""),
                "probable_cause": session.get("diagnosed_category", ""),
                "action_category": session.get("action_category", ""),
                "confidence_label": session.get("confidence_label", ""),
                "severity": session.get("severity", ""),
                "frequency": session.get("frequency", ""),
                "duration": session.get("duration", ""),
                "warning_signs": [session.get("warning_signs", "")],
                "status": status,
            }

            occurrences.append(occurrence)

            timeline_rows.append({
                "pattern_id": pattern_id,
                "date": _display_date(created_at),
                "symptom": session.get("symptom_type", ""),
                "probable_cause": session.get("diagnosed_category", ""),
                "action_category": session.get("action_category", ""),
                "confidence_label": session.get("confidence_label", ""),
                "status": status,
            })

        patterns.append({
            "id": pattern_id,
            "pattern_key": key,
            "symptom": latest.get("symptom_type", ""),
            "probable_cause": latest.get("diagnosed_category", ""),
            "occurrence_count": len(group),
            "first_detected": _display_date(first.get("created_at")),
            "latest_detected": _display_date(latest.get("created_at")),
            "previous_action": previous_action,
            "updated_action": updated_action,
            "status": status,
            "action_escalated": action_escalated,
            "recommended_next_step": (
                "This issue has appeared across multiple sessions. Review related sessions and apply only safe assisted fixes."
            ),
            "occurrences": occurrences,
            "timeline": [
                {
                    "date": _short_date(session.get("created_at")),
                    "full_date": _display_date(session.get("created_at")),
                    "session_id": session.get("session_id"),
                }
                for session in group
            ],
        })

    return {
        "metrics": {
            "recurring_issues": len(patterns),
            "worsening_trends": sum(1 for pattern in patterns if pattern["status"] == "Worsening"),
            "action_escalated": sum(1 for pattern in patterns if pattern["action_escalated"]),
            "total_occurrences": sum(pattern["occurrence_count"] for pattern in patterns),
        },
        "patterns": patterns,
        "timeline": timeline_rows,
    }