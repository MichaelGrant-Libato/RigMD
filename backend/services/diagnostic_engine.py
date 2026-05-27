import os
from uuid import uuid4
from datetime import datetime
from google import genai
from backend.config import GEMINI_API_KEY

client = genai.Client(api_key=GEMINI_API_KEY) if GEMINI_API_KEY else None


def _text(value):
    return str(value or "").lower()


def _has(value, words):
    text = _text(value)
    return any(word in text for word in words)


def _get(profile, *path, default=None):
    current = profile or {}

    for key in path:
        if not isinstance(current, dict):
            return default
        current = current.get(key)

    return current if current is not None else default


def _add(score, evidence, category, points, label, value, source="User report"):
    score[category] += points
    evidence.append({
        "category": category,
        "label": label,
        "value": value,
        "points": points,
        "source": source,
    })


def _make_proof(label, value, status, meaning):
    return {
        "label": label,
        "value": value,
        "status": status,
        "meaning": meaning,
    }


def run_diagnostic(symptom_data: dict, profile_data: dict = None) -> dict:
    symptom_type = _text(symptom_data.get("symptom_type"))
    warning_signs = _text(symptom_data.get("warning_signs"))
    recent_changes = _text(symptom_data.get("recent_changes"))
    frequency = _text(symptom_data.get("frequency"))
    severity = _text(symptom_data.get("severity"))
    affected_activity = _text(symptom_data.get("affected_activity"))
    system_state = _text(symptom_data.get("system_state"))

    cpu_usage = float(_get(profile_data, "cpu", "usage_percent", default=0) or 0)
    ram_usage = float(_get(profile_data, "ram", "usage_percent", default=0) or 0)
    disk_usage = float(_get(profile_data, "disk", "usage_percent", default=0) or 0)
    storage_type = _text(_get(profile_data, "storage_type", default=""))

    process_insights = _get(profile_data, "process_insights", default={}) or {}
    browser_memory_mb = float(process_insights.get("browser_memory_mb") or 0)
    browser_process_count = int(process_insights.get("browser_process_count") or 0)
    browser_heavy = bool(process_insights.get("browser_heavy"))
    game_detected = bool(process_insights.get("game_detected"))
    game_processes = process_insights.get("game_processes") or []
    top_memory_apps = process_insights.get("top_memory_apps") or []

    proof = []

    cpu_status = "normal"
    if cpu_usage >= 85:
        cpu_status = "high"
    elif cpu_usage >= 70:
        cpu_status = "elevated"

    ram_status = "normal"
    if ram_usage >= 85:
        ram_status = "high"
    elif ram_usage >= 75:
        ram_status = "elevated"

    disk_status = "normal"
    if disk_usage >= 90:
        disk_status = "high"
    elif disk_usage >= 80:
        disk_status = "elevated"

    browser_status = "normal"
    if browser_heavy:
        browser_status = "high"
    elif browser_memory_mb >= 512:
        browser_status = "elevated"

    proof.append(_make_proof("CPU load", f"{cpu_usage}%", cpu_status, "High CPU load can cause stuttering, freezing, and slow response."))
    proof.append(_make_proof("RAM usage", f"{ram_usage}%", ram_status, "High RAM usage can cause freezing, slow app switching, and browser lag."))
    proof.append(_make_proof("Storage usage", f"{disk_usage}% full", disk_status, "Very full storage can slow updates, file access, and system maintenance."))
    proof.append(_make_proof("Browser workload", f"{browser_memory_mb} MB across {browser_process_count} processes", browser_status, "Heavy browser memory usage can explain slowdowns with many tabs or web apps."))
    proof.append(_make_proof("Game or launcher process", ", ".join(game_processes[:3]) if game_processes else "None detected", "detected" if game_detected else "normal", "Game or launcher processes can increase GPU, CPU, memory, and thermal load."))

    objective_problem_found = any(
        item["status"] in ["elevated", "high", "detected"]
        for item in proof
    )

    strong_user_warning = (
        severity == "high"
        or frequency in ["frequent", "always"]
        or _has(warning_signs, [
            "blue screen",
            "bsod",
            "no display",
            "no boot",
            "startup repair",
            "smart",
            "caution",
            "bad",
            "access denied",
            "file not found",
        ])
    )

    if not objective_problem_found and not strong_user_warning:
        ai_explanation = (
            "RigMD did not detect active system pressure from the live scan. CPU, RAM, storage, browser workload, and game or launcher activity appear within normal ranges right now. "
            "The reported symptom may have been temporary, already resolved, or not active during this scan."
        )

        if client:
            prompt = f"""
            Explain this RigMD result in one clear paragraph for a non-technical Windows desktop user.

            Result: No active issue detected
            Proof: {proof}
            User report: {symptom_data}

            Be reassuring. Do not overstate certainty. Explain that the issue may not be active during this scan.
            """
            try:
                response = client.models.generate_content(model="gemini-2.5-flash", contents=prompt)
                ai_explanation = response.text.strip()
            except Exception:
                pass

        return {
            "session_id": str(uuid4()),
            "symptom_type": symptom_data.get("symptom_type", ""),
            "affected_activity": symptom_data.get("affected_activity", ""),
            "frequency": symptom_data.get("frequency", ""),
            "severity": symptom_data.get("severity", ""),
            "warning_signs": symptom_data.get("warning_signs", ""),
            "recent_changes": symptom_data.get("recent_changes", ""),
            "diagnosed_category": "No active issue detected",
            "action_category": "Monitor",
            "confidence_label": "High",
            "ai_explanation": ai_explanation,
            "evidence": [],
            "proof": proof,
            "system_metrics": {
                "cpu_usage_percent": cpu_usage,
                "ram_usage_percent": ram_usage,
                "disk_usage_percent": disk_usage,
                "storage_type": storage_type,
                "browser_memory_mb": browser_memory_mb,
                "browser_process_count": browser_process_count,
                "browser_heavy": browser_heavy,
                "game_detected": game_detected,
                "game_processes": game_processes,
                "top_memory_apps": top_memory_apps,
            },
            "recommended_next_step": "No active issue was detected during this scan. Monitor the system and run another diagnosis when the symptom is happening.",
            "recommendations": [],
            "is_recurring": False,
            "created_at": datetime.now().isoformat(),
        }

    score = {
        "OS performance degradation": 0,
        "Driver conflict": 0,
        "Storage health behavior": 0,
        "Boot and startup failure": 0,
        "Display driver behavior": 0,
        "Thermal condition": 0,
        "System log event flags": 0,
    }

    evidence = []

    if "os performance" in symptom_type:
        _add(score, evidence, "OS performance degradation", 2, "Reported symptom", symptom_data.get("symptom_type"))
    elif "driver" in symptom_type:
        _add(score, evidence, "Driver conflict", 2, "Reported symptom", symptom_data.get("symptom_type"))
    elif "storage" in symptom_type:
        _add(score, evidence, "Storage health behavior", 2, "Reported symptom", symptom_data.get("symptom_type"))
    elif "boot" in symptom_type or "startup" in symptom_type:
        _add(score, evidence, "Boot and startup failure", 2, "Reported symptom", symptom_data.get("symptom_type"))
    elif "display" in symptom_type or "rendering" in symptom_type:
        _add(score, evidence, "Display driver behavior", 2, "Reported symptom", symptom_data.get("symptom_type"))
    elif "thermal" in symptom_type:
        _add(score, evidence, "Thermal condition", 2, "Reported symptom", symptom_data.get("symptom_type"))

    if cpu_usage >= 85:
        _add(score, evidence, "OS performance degradation", 4, "CPU pressure detected", f"{cpu_usage}%", "Live scan")
    elif cpu_usage >= 70:
        _add(score, evidence, "OS performance degradation", 2, "CPU pressure detected", f"{cpu_usage}%", "Live scan")

    if ram_usage >= 85:
        _add(score, evidence, "OS performance degradation", 5, "RAM pressure detected", f"{ram_usage}%", "Live scan")
    elif ram_usage >= 75:
        _add(score, evidence, "OS performance degradation", 3, "RAM pressure detected", f"{ram_usage}%", "Live scan")

    if browser_heavy:
        _add(score, evidence, "OS performance degradation", 4, "Heavy browser workload detected", f"{browser_memory_mb} MB across {browser_process_count} browser processes", "Live scan")
    elif browser_memory_mb >= 512:
        _add(score, evidence, "OS performance degradation", 1, "Browser workload detected", f"{browser_memory_mb} MB", "Live scan")

    if disk_usage >= 90:
        _add(score, evidence, "Storage health behavior", 4, "Storage pressure detected", f"{disk_usage}% full", "Live scan")
        _add(score, evidence, "OS performance degradation", 2, "Storage pressure detected", f"{disk_usage}% full", "Live scan")
    elif disk_usage >= 80:
        _add(score, evidence, "OS performance degradation", 1, "Storage usage elevated", f"{disk_usage}% full", "Live scan")

    if storage_type == "hdd":
        _add(score, evidence, "OS performance degradation", 1, "Storage type", "HDD can contribute to slow loading", "Detected component")

    if game_detected and affected_activity == "gaming":
        game_value = ", ".join(game_processes[:3]) if game_processes else "Known game or launcher process detected"
        _add(score, evidence, "Thermal condition", 3, "Game or launcher workload detected", game_value, "Live scan")
        _add(score, evidence, "Display driver behavior", 2, "Graphics workload detected", game_value, "Live scan")

    if _has(warning_signs, ["blue screen", "bsod", "error code"]):
        _add(score, evidence, "Driver conflict", 4, "Warning sign", symptom_data.get("warning_signs"))
    if _has(warning_signs, ["display driver", "screen flicker", "visual"]):
        _add(score, evidence, "Display driver behavior", 4, "Warning sign", symptom_data.get("warning_signs"))
    if _has(warning_signs, ["smart", "caution", "bad", "file not found", "access denied"]):
        _add(score, evidence, "Storage health behavior", 5, "Storage warning", symptom_data.get("warning_signs"))
    if _has(warning_signs, ["startup repair", "no boot device", "black screen", "no display"]):
        _add(score, evidence, "Boot and startup failure", 4, "Boot warning", symptom_data.get("warning_signs"))

    ranked = sorted(score.items(), key=lambda item: item[1], reverse=True)
    diagnosed_category = ranked[0][0]
    top_score = ranked[0][1]
    second_score = ranked[1][1]

    if top_score >= 8 and top_score - second_score >= 2:
        confidence_label = "High"
    elif top_score >= 5:
        confidence_label = "Moderate"
    else:
        confidence_label = "Low"

    if top_score < 4:
        diagnosed_category = "No active issue detected"
        action_category = "Monitor"
        confidence_label = "Moderate"
    elif diagnosed_category in ["Driver conflict", "Boot and startup failure", "Display driver behavior", "System log event flags"]:
        action_category = "Troubleshoot"
    elif diagnosed_category in ["OS performance degradation", "Thermal condition", "Storage health behavior"]:
        action_category = "Maintain"
    else:
        action_category = "Monitor"

    if diagnosed_category == "Boot and startup failure" and (
        severity == "high" or _has(warning_signs, ["no display", "no boot device"])
    ):
        action_category = "Escalate for Professional Inspection"
        confidence_label = "High"

    evidence_for_result = [item for item in evidence if item["category"] == diagnosed_category][:6]

    ai_explanation = (
        f"RigMD identified {diagnosed_category} because the live scan and submitted symptoms point most strongly to that result."
    )

    if client:
        prompt = f"""
        Explain this RigMD diagnostic result in one clear paragraph for a non-technical Windows desktop user.

        Result: {diagnosed_category}
        Action: {action_category}
        Confidence: {confidence_label}
        Evidence: {evidence_for_result}
        Proof from live scan: {proof}
        Current system data: CPU {cpu_usage}%, RAM {ram_usage}%, Disk {disk_usage}%, Storage type {storage_type}
        Process workload: Browser memory {browser_memory_mb} MB, browser processes {browser_process_count}, browser heavy {browser_heavy}, game detected {game_detected}, game processes {game_processes}, top memory apps {top_memory_apps[:3]}
        User notes: {system_state}

        Do not overstate certainty. Mention when evidence comes from live system data.
        """
        try:
            response = client.models.generate_content(model="gemini-2.5-flash", contents=prompt)
            ai_explanation = response.text.strip()
        except Exception as error:
            ai_explanation = f"AI explanation unavailable. Rule-based result: {diagnosed_category}. Details: {error}"

    return {
        "session_id": str(uuid4()),
        "symptom_type": symptom_data.get("symptom_type", ""),
        "affected_activity": symptom_data.get("affected_activity", ""),
        "frequency": symptom_data.get("frequency", ""),
        "severity": symptom_data.get("severity", ""),
        "warning_signs": symptom_data.get("warning_signs", ""),
        "recent_changes": symptom_data.get("recent_changes", ""),
        "diagnosed_category": diagnosed_category,
        "action_category": action_category,
        "confidence_label": confidence_label,
        "ai_explanation": ai_explanation,
        "evidence": evidence_for_result,
        "proof": proof,
        "system_metrics": {
            "cpu_usage_percent": cpu_usage,
            "ram_usage_percent": ram_usage,
            "disk_usage_percent": disk_usage,
            "storage_type": storage_type,
            "browser_memory_mb": browser_memory_mb,
            "browser_process_count": browser_process_count,
            "browser_heavy": browser_heavy,
            "game_detected": game_detected,
            "game_processes": game_processes,
            "top_memory_apps": top_memory_apps,
        },
        "recommended_next_step": (
            "No active issue was detected during this scan. Monitor the system and run another diagnosis when the symptom is happening."
            if diagnosed_category == "No active issue detected"
            else f"RigMD recommends the {action_category} path for {diagnosed_category}. Review the proof, apply only available safe fixes, then re-run the diagnostic check."
        ),
        "recommendations": [],
        "is_recurring": False,
        "created_at": datetime.now().isoformat(),
    }