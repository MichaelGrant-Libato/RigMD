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
        "warning_sign": "Stuttering or Freezing",
        "meaning": "The system is struggling to keep up with active work, often because of RAM pressure, CPU load, browser load, or background apps.",
        "threshold": "Repeated cursor freezes, app hangs, short system pauses, or noticeable stuttering during normal use.",
        "action": "Maintain",
        "category": "Performance",
        "keywords": ["stuttering", "stutter", "freeze", "freezing", "lag", "slow", "hitch", "hang", "unresponsive"],
    },
    {
        "id": "WS-002",
        "warning_sign": "Blue Screen or Crash Code",
        "meaning": "A Windows crash may indicate driver conflict, memory instability, or system-level failure.",
        "threshold": "Any repeated blue screen, restart loop, or visible stop code.",
        "action": "Troubleshoot",
        "category": "Crash / Stability",
        "keywords": ["blue screen", "bsod", "stop code", "crash", "restart", "critical crash"],
    },
    {
        "id": "WS-003",
        "warning_sign": "Display Driver or Visual Glitch",
        "meaning": "Graphics driver instability may cause flicker, black screens, artifacts, or crashes during visual workloads.",
        "threshold": "Repeated flickering, black screen recovery, visual artifacts, or display-driver warnings.",
        "action": "Troubleshoot",
        "category": "Drivers / Display",
        "keywords": ["display driver", "flicker", "screen flicker", "black screen", "visual", "artifact", "glitch", "screen tearing"],
    },
    {
        "id": "WS-004",
        "warning_sign": "No Display",
        "meaning": "The device may be failing before Windows fully loads, or the display path may have a hardware-level issue.",
        "threshold": "Power is on but the screen remains black or no signal appears.",
        "action": "Escalate",
        "category": "Boot / Display",
        "keywords": ["no display", "black screen", "no signal"],
    },
    {
        "id": "WS-005",
        "warning_sign": "No Boot Device or Startup Repair",
        "meaning": "Windows may not be reaching the operating system because of boot configuration, storage, or system file issues.",
        "threshold": "No boot device message, startup repair loop, or repeated failure before desktop.",
        "action": "Troubleshoot",
        "category": "Boot",
        "keywords": ["no boot device", "startup repair", "boot loop", "automatic repair", "won't start", "startup"],
    },
    {
        "id": "WS-006",
        "warning_sign": "Storage SMART Caution or File Access Error",
        "meaning": "The storage device may be reporting health warnings or file access failures. Backups should be prioritized before deeper repair steps.",
        "threshold": "SMART caution, bad status, file access errors, or repeated save/open failures.",
        "action": "Escalate",
        "category": "Storage",
        "keywords": ["smart", "caution", "bad", "file not found", "access denied", "save error", "storage warning", "disk health"],
    },
    {
        "id": "WS-007",
        "warning_sign": "Loud Fan Noise or Heat",
        "meaning": "The system may be reacting to heat or sustained workload. This is common during gaming or heavy multitasking.",
        "threshold": "Fans suddenly become loud, the device feels hot, or performance drops during heavy use.",
        "action": "Maintain",
        "category": "Thermal",
        "keywords": ["loud fan", "fan noise", "overheat", "overheating", "hot", "thermal", "throttling"],
    },
    {
        "id": "WS-008",
        "warning_sign": "Windows Update Failure",
        "meaning": "Repeated Windows update failures may point to OS corruption, storage issues, or service errors.",
        "threshold": "Two or more failed update attempts.",
        "action": "Troubleshoot",
        "category": "OS",
        "keywords": ["windows update", "update failed", "update failure"],
    },
    {
        "id": "WS-009",
        "warning_sign": "Application Crashes",
        "meaning": "One or more applications are crashing repeatedly during normal use.",
        "threshold": "Repeated app crashes in the same session or across multiple sessions.",
        "action": "Troubleshoot",
        "category": "Software",
        "keywords": ["app crash", "application crash", "program crash", "crashes", "error message"],
    },
    {
        "id": "WS-010",
        "warning_sign": "Slow Boot or Startup Delay",
        "meaning": "Startup delay may indicate storage pressure, startup app load, driver delays, or OS-level performance issues.",
        "threshold": "Boot time feels unusually long or exceeds about 60 seconds.",
        "action": "Maintain",
        "category": "Boot",
        "keywords": ["slow boot", "boot time", "startup delay", "startup slow"],
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