from typing import Any

from fastapi import APIRouter, Depends, Query
from sqlalchemy.exc import SQLAlchemyError
from sqlalchemy.orm import Session as DbSession

from backend.database import get_db
from backend.models.session_model import Session as DiagnosticSession
from backend.models.session_model import Recommendation


router = APIRouter(prefix="/api/warning-signs", tags=["Warning Signs"])


WARNING_SIGNS_REFERENCE = [
    {
        "id": "WS-001",
        "warning_sign": "Repeated Blue Screen Error Codes",
        "meaning": "Critical system crash caused by hardware, driver, memory, or kernel-level failure.",
        "threshold": "2+ occurrences within 7 days",
        "action": "Escalate",
        "category": "System Crash",
        "keywords": ["blue screen", "bsod", "stop code", "critical crash"],
    },
    {
        "id": "WS-002",
        "warning_sign": "Windows Event Log Critical Errors",
        "meaning": "OS-level failures logged during boot, shutdown, or normal operation.",
        "threshold": "3+ critical events in Event Viewer",
        "action": "Troubleshoot",
        "category": "System Logs",
        "keywords": ["event log", "critical event", "event viewer", "kernel power"],
    },
    {
        "id": "WS-003",
        "warning_sign": "Storage Health Status Downgraded",
        "meaning": "SMART data indicates possible physical drive degradation or storage reliability issue.",
        "threshold": "Any SMART warning flag",
        "action": "Escalate",
        "category": "Storage",
        "keywords": ["smart", "storage warning", "drive health", "disk health"],
    },
    {
        "id": "WS-004",
        "warning_sign": "Driver Conflict Notification",
        "meaning": "Device Manager shows a driver conflict, missing driver, or device warning.",
        "threshold": "Any yellow warning icon in Device Manager",
        "action": "Troubleshoot",
        "category": "Drivers",
        "keywords": ["driver conflict", "device manager", "yellow warning", "driver issue"],
    },
    {
        "id": "WS-005",
        "warning_sign": "Startup Repair Prompt",
        "meaning": "Windows failed to boot normally and entered recovery or repair mode.",
        "threshold": "1 occurrence",
        "action": "Escalate",
        "category": "Boot",
        "keywords": ["startup repair", "repair loop", "automatic repair", "boot repair"],
    },
    {
        "id": "WS-006",
        "warning_sign": "Recurring System Freeze",
        "meaning": "System becomes unresponsive and requires a force restart or hard reset.",
        "threshold": "2+ times per week",
        "action": "Troubleshoot",
        "category": "Performance",
        "keywords": ["freeze", "system freezing", "unresponsive", "hard reset"],
    },
    {
        "id": "WS-007",
        "warning_sign": "No Display Output After OS Login",
        "meaning": "Screen becomes black after Windows loads, suggesting display driver or GPU issue.",
        "threshold": "1 occurrence",
        "action": "Escalate",
        "category": "Display",
        "keywords": ["black screen", "no display", "display output", "gpu display"],
    },
    {
        "id": "WS-008",
        "warning_sign": "Repeated Windows Update Failure",
        "meaning": "Windows updates fail repeatedly, possibly due to OS corruption, storage issues, or service errors.",
        "threshold": "2+ failed update attempts",
        "action": "Troubleshoot",
        "category": "OS",
        "keywords": ["windows update", "update failed", "update failure"],
    },
    {
        "id": "WS-009",
        "warning_sign": "Repeated Application Crashes",
        "meaning": "One or more applications crash consistently during use.",
        "threshold": "3+ crashes of the same app within a session",
        "action": "Troubleshoot",
        "category": "Software",
        "keywords": ["app crash", "application crash", "program crash", "crashes"],
    },
    {
        "id": "WS-010",
        "warning_sign": "CPU Running Above Safe Operating Range",
        "meaning": "High CPU temperature may cause thermal throttling, shutdowns, or long-term component stress.",
        "threshold": "Sustained temperature above safe limit",
        "action": "Troubleshoot",
        "category": "Thermal",
        "keywords": ["cpu temperature", "overheating", "thermal", "throttling"],
    },
    {
        "id": "WS-011",
        "warning_sign": "Boot Time Exceeding 60 Seconds",
        "meaning": "Startup delay may indicate storage, startup program, driver, or OS-level performance issues.",
        "threshold": "Boot time above 60 seconds",
        "action": "Maintain",
        "category": "Boot",
        "keywords": ["slow boot", "boot time", "startup delay"],
    },
    {
        "id": "WS-012",
        "warning_sign": "Disk Activity at 100% on Startup",
        "meaning": "High disk usage during startup may indicate background services, failing storage, or OS indexing issues.",
        "threshold": "Disk active time near 100% after startup",
        "action": "Maintain",
        "category": "Storage",
        "keywords": ["100% disk", "disk activity", "high disk usage"],
    },
]


def normalize_text(value: str | None) -> str:
    if not value:
        return ""

    return value.strip().lower()


def split_warning_signs(raw_warning_signs: str | None) -> list[str]:
    if not raw_warning_signs:
        return []

    cleaned = raw_warning_signs.replace(";", "\n").replace(",", "\n")
    return [item.strip() for item in cleaned.splitlines() if item.strip()]


def collect_observed_warning_texts(db: DbSession) -> list[str]:
    observed_texts: list[str] = []

    recommendation_rows = db.query(Recommendation).all()

    for row in recommendation_rows:
        if row.warning_sign:
            observed_texts.append(row.warning_sign)

    session_rows = db.query(DiagnosticSession).all()

    for row in session_rows:
        observed_texts.extend(split_warning_signs(row.warning_signs))

    return observed_texts


def get_observed_count(reference: dict[str, Any], observed_texts: list[str]) -> int:
    warning_name = normalize_text(reference["warning_sign"])
    keywords = [normalize_text(keyword) for keyword in reference.get("keywords", [])]

    count = 0

    for observed in observed_texts:
        observed_value = normalize_text(observed)

        if not observed_value:
            continue

        if warning_name in observed_value or observed_value in warning_name:
            count += 1
            continue

        if any(keyword and keyword in observed_value for keyword in keywords):
            count += 1

    return count


def build_reference_rows(
    observed_texts: list[str],
    category: str,
    search: str,
    observed_only: bool,
) -> list[dict[str, Any]]:
    rows = []

    category_filter = normalize_text(category)
    search_filter = normalize_text(search)

    for reference in WARNING_SIGNS_REFERENCE:
        observed_count = get_observed_count(reference, observed_texts)
        row = {
            **reference,
            "observed": observed_count > 0,
            "observed_count": observed_count,
        }

        category_matches = (
            not category_filter
            or category_filter == "all"
            or normalize_text(row["category"]) == category_filter
        )

        searchable_text = " ".join(
            [
                row["warning_sign"],
                row["meaning"],
                row["threshold"],
                row["action"],
                row["category"],
            ]
        ).lower()

        search_matches = not search_filter or search_filter in searchable_text
        observed_matches = not observed_only or row["observed"]

        if category_matches and search_matches and observed_matches:
            rows.append(row)

    return rows


@router.get("/reference")
def get_warning_signs_reference(
    category: str = Query(default="all"),
    search: str = Query(default=""),
    observed_only: bool = Query(default=False),
    db: DbSession = Depends(get_db),
):
    try:
        observed_texts = collect_observed_warning_texts(db)
        rows = build_reference_rows(
            observed_texts=observed_texts,
            category=category,
            search=search,
            observed_only=observed_only,
        )

        observed_count = len([row for row in rows if row["observed"]])
        total_observed_count = sum(row["observed_count"] for row in rows)

        categories = sorted({row["category"] for row in WARNING_SIGNS_REFERENCE})

        return {
            "summary": {
                "observed_warning_signs": observed_count,
                "total_observed_occurrences": total_observed_count,
                "total_reference_items": len(WARNING_SIGNS_REFERENCE),
            },
            "categories": categories,
            "warning_signs": rows,
        }

    except SQLAlchemyError as error:
        rows = build_reference_rows(
            observed_texts=[],
            category=category,
            search=search,
            observed_only=observed_only,
        )

        return {
            "summary": {
                "observed_warning_signs": 0,
                "total_observed_occurrences": 0,
                "total_reference_items": len(WARNING_SIGNS_REFERENCE),
            },
            "categories": sorted({row["category"] for row in WARNING_SIGNS_REFERENCE}),
            "warning_signs": rows,
            "database_warning": str(error),
        }