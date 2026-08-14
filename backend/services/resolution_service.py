from datetime import datetime


def _get(data, *path, default=None):
    current = data or {}
    for key in path:
        if not isinstance(current, dict):
            return default
        current = current.get(key)
    return current if current is not None else default


def _proof(label, value, status, meaning):
    return {
        "label": label,
        "value": value,
        "status": status,
        "meaning": meaning,
    }


def check_os_performance_resolution(profile_data: dict) -> dict:
    ram_usage = float(_get(profile_data, "ram", "usage_percent", default=0) or 0)

    process_insights = _get(profile_data, "process_insights", default={}) or {}
    browser_memory_mb = float(process_insights.get("browser_memory_mb") or 0)
    browser_process_count = int(process_insights.get("browser_process_count") or 0)

    ram_ok = ram_usage < 75
    browser_ok = browser_memory_mb < 2048 and browser_process_count < 20
    resolved = ram_ok and browser_ok

    return {
        "resolution_status": "resolved" if resolved else "still_active",
        "resolution_checked_at": datetime.now().isoformat(),
        "resolution_summary": (
            "The main RAM and browser pressure is no longer showing in the live scan."
            if resolved
            else "The same RAM or browser pressure is still showing in the live scan."
        ),
        "resolution_proof": [
            _proof(
                "RAM usage",
                f"{ram_usage}%",
                "resolved" if ram_ok else "still high",
                "Below 75% is treated as resolved for this diagnosis.",
            ),
            _proof(
                "Browser workload",
                f"{browser_memory_mb} MB across {browser_process_count} processes",
                "resolved" if browser_ok else "still high",
                "Browser pressure is resolved when memory is below 2048 MB and process count is below 20.",
            ),
        ],
    }


def check_resolution_for_diagnosis(diagnosis_record: dict, profile_data: dict) -> dict:
    category = str(diagnosis_record.get("diagnosed_category", "")).lower()

    if "os performance" in category:
        return check_os_performance_resolution(profile_data)

    return {
        "resolution_status": "needs_recheck",
        "resolution_checked_at": datetime.now().isoformat(),
        "resolution_summary": "This diagnosis type needs a follow-up symptom answer before RigMD can call it resolved.",
        "resolution_proof": [],
    }