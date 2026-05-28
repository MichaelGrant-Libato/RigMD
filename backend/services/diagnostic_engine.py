from uuid import uuid4
from datetime import datetime
from google import genai
from backend.config import GEMINI_API_KEY

client = genai.Client(api_key=GEMINI_API_KEY) if GEMINI_API_KEY else None


def _text(value):
    return str(value or "").lower().strip()


def _has(value, words):
    text = _text(value)
    return any(word in text for word in words)


def _flag(signals, key):
    return bool((signals or {}).get(key))


def _joined_text(*values):
    return " ".join(_text(value) for value in values if _text(value))


def _get(profile, *path, default=None):
    current = profile or {}
    for key in path:
        if not isinstance(current, dict):
            return default
        current = current.get(key)
    return current if current is not None else default


def _add(score, evidence, category, points, label, value, source="User report"):
    if category not in score:
        return

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


def _reported_category(symptom_type):
    if "os performance" in symptom_type:
        return "OS performance degradation"
    if "driver" in symptom_type:
        return "Driver conflict"
    if "storage" in symptom_type:
        return "Storage health behavior"
    if "boot" in symptom_type or "startup" in symptom_type:
        return "Boot and startup failure"
    if "display" in symptom_type or "rendering" in symptom_type:
        return "Display driver behavior"
    if "thermal" in symptom_type:
        return "Thermal condition"
    return None


def _clean_ai_text(value):
    text = " ".join(str(value or "").split())
    if not text:
        return ""

    blocked = ["traceback", "exception", "api key", "google.api", "gemini api error", "quota", "deadline"]
    if any(fragment in text.lower() for fragment in blocked):
        return ""

    return text


def _proof_status(proof, label):
    for item in proof:
        if item["label"].lower() == label.lower():
            return item["status"].lower()
    return "normal"


def _proof_value(proof, label):
    for item in proof:
        if item["label"].lower() == label.lower():
            return item["value"]
    return ""


def _has_pressure(proof, *labels):
    return any(_proof_status(proof, label) in ["elevated", "high"] for label in labels)


def _evidence_has(evidence, words):
    combined = " ".join(f"{item.get('label', '')} {item.get('value', '')}" for item in evidence)
    return _has(combined, words)


def _build_no_issue_explanation(proof):
    normal_items = [
        f"{item['label']} is {item['value']}"
        for item in proof
        if item["status"].lower() in ["normal", "observed"]
    ]

    detail = ", ".join(normal_items[:4]) or "the live readings are not showing active pressure"

    return (
        f"RigMD did not find an active problem in the live scan right now. {detail}. "
        "That means the issue may not be happening at this exact moment, or it may have already cleared after a restart or after a background task finished. "
        "Use the computer normally, and run another check while the symptom is actually happening if it comes back."
    )


def _build_plain_explanation(diagnosed_category, action_category, confidence_label, evidence, proof):
    category = diagnosed_category.lower()
    live_findings = []

    if "os performance" in category:
        if _has_pressure(proof, "CPU load"):
            live_findings.append(f"CPU load is {_proof_value(proof, 'CPU load')}")
        if _has_pressure(proof, "RAM usage"):
            live_findings.append(f"RAM usage is {_proof_value(proof, 'RAM usage')}")
        if _has_pressure(proof, "Browser workload"):
            live_findings.append(f"browser workload is {_proof_value(proof, 'Browser workload')}")
        if _has_pressure(proof, "Storage usage"):
            live_findings.append(f"storage is {_proof_value(proof, 'Storage usage')}")

    elif "storage" in category:
        if _has_pressure(proof, "Storage usage"):
            live_findings.append(f"storage is {_proof_value(proof, 'Storage usage')}")

    elif "thermal" in category:
        if _has_pressure(proof, "CPU load"):
            live_findings.append(f"CPU load is {_proof_value(proof, 'CPU load')}")
        game = _proof_value(proof, "Game or launcher process")
        if game and game != "None detected":
            live_findings.append(f"a game or launcher process was detected: {game}")

    if live_findings:
        reason = ", ".join(live_findings)
    elif evidence:
        reason = ", ".join([f"{item['label']}: {item['value']}" for item in evidence[:3]])
    else:
        reason = "your answers point in this direction, but the live scan did not catch a strong matching reading"

    if diagnosed_category == "OS performance degradation":
        if _has_pressure(proof, "Storage usage"):
            intro = "Your computer is most likely slowing down because Windows is under load from memory, apps, browser tabs, startup programs, or storage pressure."
        else:
            intro = "Your computer is most likely slowing down because Windows is under load from memory, apps, browser tabs, or startup programs."
    elif diagnosed_category == "Storage health behavior":
        if _has_pressure(proof, "Storage usage"):
            intro = "This points toward storage pressure, so file access, saving, updates, or drive checks may be involved."
        else:
            intro = "Your answers point toward a storage or file-access slowdown, even though the drive is not currently showing as very full."
    elif diagnosed_category == "Driver conflict":
        intro = "This looks like a driver-related issue, which means Windows and one hardware part may not be communicating cleanly."
    elif diagnosed_category == "Boot and startup failure":
        intro = "This points toward a startup or wake-from-sleep problem, especially when the screen stays black, startup loops, or Windows does not fully load."
    elif diagnosed_category == "Display driver behavior":
        intro = "This looks related to the display path, so the graphics driver, screen output, or a graphics-heavy app may be involved."
    elif diagnosed_category == "Thermal condition":
        intro = "This looks like heat or cooling pressure. When a PC gets too warm, it can slow itself down to protect the parts inside."
    else:
        intro = "RigMD found a likely issue based on the live scan and the details you entered."

    return (
        f"{intro} RigMD chose this result because {reason}. "
        f"The confidence is {confidence_label.lower()}, so treat this as the best next clue rather than a guaranteed final answer. "
        f"The safest path is to follow the {action_category.lower()} recommendation below and then run the check again after the change."
    )


def _get_recommended_next_step(diagnosed_category, action_category, proof, evidence):
    category = diagnosed_category.lower()

    if "no active issue" in category:
        return "No active pressure showed up in this scan. Use the computer normally, and run another diagnosis while the symptom is happening if it comes back."

    if "boot" in category or "startup" in category:
        if _evidence_has(evidence, ["black screen", "no display", "no boot", "startup repair"]):
            return "If the screen stays black or Windows cannot fully start, stop repeated restarts. Try Windows Startup Repair if available, or ask a technician to inspect it. If Windows still opens, check Startup Apps and recent Windows updates."
        return "If Windows still opens, review Startup Apps first. If the startup delay continues, restart once and run the diagnosis again while the issue is happening."

    if "os performance" in category:
        if _has_pressure(proof, "RAM usage", "Browser workload"):
            return "Close unused browser tabs and heavy apps first, then use Verify & Inspect to open Task Manager. If the PC still feels slow, apply the available safe maintenance actions and run the diagnosis again."
        return "Review startup apps and temporary files, then restart the PC and run the diagnosis again while the slowdown is happening."

    if "thermal" in category:
        return "Pause games or heavy apps, let the PC cool for a few minutes, and check that vents and fans are not blocked. If it shuts down, smells hot, or shows no display, stop testing and ask a technician to inspect it."

    if "driver" in category:
        return "Open Device Manager and look for warning icons. Only update, roll back, or reinstall the device that matches the symptom, especially if the problem started after a driver or Windows update."

    if "display" in category:
        return "Try the safe display-driver reset shortcut first. If flickering or black screens continue, open Device Manager and review the display adapter driver."

    if "storage" in category:
        return "Back up important files first. Then open Storage settings or run the read-only disk scan. Do not run repair commands until your important files are backed up."

    if "escalate" in action_category.lower():
        return "Stop repeated testing and bring this result to a qualified technician, especially if the computer cannot boot, overheats, or shows hardware warning signs."

    return "Review the proof shown above, use the safest available action, and run the diagnosis again after the change."


def _get_verification_target(diagnosed_category, proof, evidence):
    category = diagnosed_category.lower()

    if "no active issue" in category:
        return {
            "target": "none",
            "label": "No verification target needed",
            "description": "The live scan did not find an active issue that needs a Windows tool right now.",
        }

    if "boot" in category or "startup" in category:
        if _evidence_has(evidence, ["black screen", "no display", "no boot", "startup repair"]):
            return {
                "target": "reliability_monitor",
                "label": "Windows Reliability Monitor",
                "description": "Use this if Windows still opens. It can show recent startup failures, crashes, or update-related problems.",
            }

        return {
            "target": "startup_apps",
            "label": "Windows Startup Apps settings",
            "description": "Review apps that run during startup.",
        }

    if "os performance" in category:
        if _has_pressure(proof, "RAM usage", "CPU load", "Browser workload"):
            return {
                "target": "task_manager",
                "label": "Task Manager - Processes tab",
                "description": "Check which apps are using the most memory, CPU, or browser processes.",
            }

        return {
            "target": "startup_apps",
            "label": "Windows Startup Apps settings",
            "description": "Review apps that automatically run when Windows starts.",
        }

    if "thermal" in category:
        return {
            "target": "task_manager",
            "label": "Task Manager - Performance tab",
            "description": "Check whether a game, browser, or app is keeping the PC under heavy load.",
        }

    if "storage" in category:
        return {
            "target": "storage_settings",
            "label": "Windows Storage settings",
            "description": "Inspect drive space before running any read-only disk check.",
        }

    if "display" in category:
        return {
            "target": "device_manager",
            "label": "Device Manager - Display adapters",
            "description": "Inspect the display adapter driver without changing it automatically.",
        }

    if "driver" in category:
        return {
            "target": "device_manager",
            "label": "Windows Device Manager",
            "description": "Look for warning icons on hardware devices.",
        }

    return {
        "target": "reliability_monitor",
        "label": "Windows Reliability Monitor",
        "description": "Review recent crashes and Windows error events.",
    }

def _proof_by_label(proof, label):
    for item in proof:
        if item["label"].lower() == label.lower():
            return item
    return None


def _make_user_proof(label, value, meaning):
    return _make_proof(label, value or "Not specified", "detected", meaning)


def _select_display_proof(diagnosed_category, proof, evidence):
    category = diagnosed_category.lower()

    if "no active issue" in category:
        return [
            item for item in proof
            if item["label"] in ["CPU load", "RAM usage", "Storage usage", "Browser workload"]
        ]

    display = []

    if "os performance" in category:
        for label in ["CPU load", "RAM usage", "Browser workload", "Storage usage"]:
            item = _proof_by_label(proof, label)
            if item and item["status"].lower() in ["elevated", "high"]:
                display.append(item)

    elif "thermal" in category:
        game = _proof_by_label(proof, "Game or launcher process")
        cpu = _proof_by_label(proof, "CPU load")
        if game and game["status"].lower() != "normal":
            display.append(game)
        if cpu and cpu["status"].lower() in ["elevated", "high"]:
            display.append(cpu)

    elif "storage" in category:
        storage = _proof_by_label(proof, "Storage usage")
        if storage:
            if storage["status"].lower() in ["elevated", "high"]:
                display.append(storage)
            else:
                display.append(_make_proof(
                    "Storage space checked",
                    storage["value"],
                    "observed",
                    "The drive is not very full right now, so this result is based more on your file-access symptoms than low free space."
                ))

    for item in evidence:
        if item["source"] == "Live scan":
            continue

        if item["category"].lower() == category:
            display.append(_make_user_proof(
                item["label"],
                item["value"],
                "This answer directly influenced the diagnostic result."
            ))

    if not display:
        display = [
            _make_user_proof(
                "Submitted symptom pattern",
                diagnosed_category,
                "The result is mainly based on your selected answers and written description, not a strong live hardware reading."
            )
        ]

    return display[:6]


def run_diagnostic(symptom_data: dict, profile_data: dict = None) -> dict:
    symptom_type = _text(symptom_data.get("symptom_type"))
    warning_signs = _text(symptom_data.get("warning_signs"))
    recent_changes = _text(symptom_data.get("recent_changes"))
    frequency = _text(symptom_data.get("frequency"))
    severity = _text(symptom_data.get("severity"))
    affected_activity = _text(symptom_data.get("affected_activity"))
    system_state = _text(symptom_data.get("system_state"))

    warning_signs_label = _text(symptom_data.get("warning_signs_label"))
    recent_changes_label = _text(symptom_data.get("recent_changes_label"))
    affected_activity_label = _text(symptom_data.get("affected_activity_label"))
    system_state_signals = symptom_data.get("system_state_signals") or {}

    warning_text = _joined_text(warning_signs, warning_signs_label, system_state)
    recent_changes_text = _joined_text(recent_changes, recent_changes_label, system_state)
    activity_text = _joined_text(affected_activity, affected_activity_label, system_state)

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

    cpu_status = "high" if cpu_usage >= 85 else "elevated" if cpu_usage >= 70 else "normal"
    ram_status = "high" if ram_usage >= 85 else "elevated" if ram_usage >= 75 else "normal"
    disk_status = "high" if disk_usage >= 90 else "elevated" if disk_usage >= 80 else "normal"

    if browser_memory_mb >= 2048 or browser_process_count >= 20:
        browser_status = "high"
    elif browser_heavy or browser_memory_mb >= 1024 or browser_process_count >= 10:
        browser_status = "elevated"
    elif browser_memory_mb > 0:
        browser_status = "observed"
    else:
        browser_status = "normal"

    game_status = "observed" if game_detected else "normal"

    proof.append(_make_proof("CPU load", f"{cpu_usage}%", cpu_status, "High CPU load can cause stuttering, freezing, and slow response."))
    proof.append(_make_proof("RAM usage", f"{ram_usage}%", ram_status, "High RAM usage can cause freezing, slow app switching, and browser lag."))
    proof.append(_make_proof("Storage usage", f"{disk_usage}% full", disk_status, "Very full storage can slow updates, file access, and system maintenance."))
    proof.append(_make_proof("Browser workload", f"{browser_memory_mb} MB across {browser_process_count} processes", browser_status, "Heavy browser memory usage can explain slowdowns with many tabs or web apps."))
    proof.append(_make_proof("Game or launcher process", ", ".join(game_processes[:3]) if game_processes else "None detected", game_status, "Game or launcher processes can increase GPU, CPU, memory, and heat while they are open."))

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
    reported_category = _reported_category(symptom_type)

    critical_warning = (
        _has(warning_text, [
            "blue screen",
            "bsod",
            "stop code",
            "no display",
            "black screen",
            "screen stays black",
            "screen remains black",
            "remains black",
            "screen goes black",
            "no boot",
            "boot loop",
            "startup repair",
            "smart",
            "caution",
            "bad",
            "access denied",
            "file not found",
            "display driver",
        ])
        or _flag(system_state_signals, "mentions_black_screen")
        or _flag(system_state_signals, "mentions_no_boot")
        or _flag(system_state_signals, "mentions_blue_screen")
        or _flag(system_state_signals, "mentions_storage")
    )

    objective_problem_found = any(item["status"] in ["elevated", "high"] for item in proof)
    strong_user_warning = critical_warning or (severity == "high" and frequency in ["frequent", "always"])

    if reported_category:
        _add(score, evidence, reported_category, 2 if strong_user_warning else 1, "Reported symptom", symptom_data.get("symptom_type"))
    if reported_category == "Storage health behavior":
        _add(
            score,
            evidence,
            "Storage health behavior",
            3,
            "Storage issue selected",
            symptom_data.get("symptom_type"),
        )

    if severity == "high" and reported_category:
        _add(score, evidence, reported_category, 1, "High disruption", symptom_data.get("severity"))

    if frequency in ["frequent", "always"] and reported_category:
        _add(score, evidence, reported_category, 1, "Frequent issue", symptom_data.get("frequency"))
    if reported_category == "Storage health behavior":
        if _has(activity_text, [
            "moving files",
            "opening folders",
            "saving work",
            "saving",
            "save",
            "file access",
            "folders",
            "files",
            "loading maps",
            "textures",
        ]):
            _add(
                score,
                evidence,
                "Storage health behavior",
                4,
                "Storage activity",
                symptom_data.get("affected_activity_label") or symptom_data.get("affected_activity"),
            )

        if _has(warning_text, [
            "searching",
            "saving",
            "save",
            "file",
            "folder",
            "access denied",
            "file not found",
            "drive",
            "disk",
            "storage",
            "freezes while searching",
        ]):
            _add(
                score,
                evidence,
                "Storage health behavior",
                4,
                "Storage warning",
                symptom_data.get("warning_signs_label") or symptom_data.get("warning_signs") or symptom_data.get("system_state"),
            )

        if _has(recent_changes_text, [
            "huge program",
            "configuration archives",
            "large program",
            "new software",
            "new software installed",
            "downloaded",
        ]):
            _add(
                score,
                evidence,
                "Storage health behavior",
                2,
                "Recent storage-heavy change",
                symptom_data.get("recent_changes_label") or symptom_data.get("recent_changes"),
            )

    if cpu_usage >= 85:
        _add(score, evidence, "OS performance degradation", 4, "CPU pressure detected", f"{cpu_usage}%", "Live scan")
    elif cpu_usage >= 70:
        _add(score, evidence, "OS performance degradation", 2, "CPU pressure detected", f"{cpu_usage}%", "Live scan")

    if ram_usage >= 85:
        _add(score, evidence, "OS performance degradation", 5, "RAM pressure detected", f"{ram_usage}%", "Live scan")
    elif ram_usage >= 75:
        _add(score, evidence, "OS performance degradation", 3, "RAM pressure detected", f"{ram_usage}%", "Live scan")

    if browser_status == "high":
        _add(score, evidence, "OS performance degradation", 4, "Heavy browser workload detected", f"{browser_memory_mb} MB across {browser_process_count} browser processes", "Live scan")
    elif browser_status == "elevated":
        _add(score, evidence, "OS performance degradation", 2, "Browser workload elevated", f"{browser_memory_mb} MB across {browser_process_count} browser processes", "Live scan")

    if disk_usage >= 90:
        _add(score, evidence, "Storage health behavior", 4, "Storage pressure detected", f"{disk_usage}% full", "Live scan")
        _add(score, evidence, "OS performance degradation", 2, "Storage pressure detected", f"{disk_usage}% full", "Live scan")
    elif disk_usage >= 80:
        _add(score, evidence, "OS performance degradation", 1, "Storage usage elevated", f"{disk_usage}% full", "Live scan")

    if storage_type == "hdd" and affected_activity in ["startup", "working"]:
        _add(score, evidence, "OS performance degradation", 1, "Storage type", "HDD can contribute to slow loading", "Detected component")

    if game_detected and affected_activity == "gaming":
        game_value = ", ".join(game_processes[:3]) if game_processes else "Known game or launcher process detected"

        if reported_category == "Thermal condition" or _flag(system_state_signals, "mentions_heat"):
            _add(score, evidence, "Thermal condition", 2, "Game workload observed", game_value, "Live scan")

        if reported_category == "Display driver behavior" or _flag(system_state_signals, "mentions_flicker"):
            _add(score, evidence, "Display driver behavior", 2, "Graphics workload observed", game_value, "Live scan")

    if _has(warning_text, ["blue screen", "bsod", "error code", "stop code"]) or _flag(system_state_signals, "mentions_blue_screen"):
        _add(score, evidence, "Driver conflict", 5, "Crash warning", symptom_data.get("warning_signs_label") or symptom_data.get("warning_signs") or symptom_data.get("system_state"))

    if _has(warning_text, ["display driver", "screen flicker", "visual", "flicker", "screen tearing", "glitch"]) or _flag(system_state_signals, "mentions_flicker"):
        _add(score, evidence, "Display driver behavior", 5, "Display warning", symptom_data.get("warning_signs_label") or symptom_data.get("warning_signs") or symptom_data.get("system_state"))

    if _has(warning_text, ["smart", "caution", "bad", "file not found", "access denied"]) or _flag(system_state_signals, "mentions_storage"):
        _add(score, evidence, "Storage health behavior", 5, "Storage warning", symptom_data.get("warning_signs_label") or symptom_data.get("warning_signs") or symptom_data.get("system_state"))

    if _has(warning_text, ["startup repair", "no boot device", "black screen", "no display", "remains black", "screen goes black", "screen stays black"]) or _flag(system_state_signals, "mentions_black_screen") or _flag(system_state_signals, "mentions_no_boot"):
        _add(score, evidence, "Boot and startup failure", 6, "Boot warning", symptom_data.get("warning_signs_label") or symptom_data.get("warning_signs") or symptom_data.get("system_state"))

    if (_has(warning_text, ["loud fan noise", "hot", "overheat", "overheating", "very warm"]) or _flag(system_state_signals, "mentions_heat")) and reported_category == "Thermal condition":
        _add(score, evidence, "Thermal condition", 4, "Heat or cooling warning", symptom_data.get("warning_signs_label") or symptom_data.get("system_state"))

    if (_has(warning_text, ["slow", "lag", "freeze", "freezing", "stutter", "stuttering"]) or _flag(system_state_signals, "mentions_slow")) and reported_category == "OS performance degradation":
        _add(score, evidence, "OS performance degradation", 3, "Slowdown described", symptom_data.get("system_state") or symptom_data.get("warning_signs_label"))

    if reported_category == "Boot and startup failure":
        if _has(activity_text, ["startup", "boot", "sleep", "hibernate", "hibernation", "turning on", "resume"]):
            _add(score, evidence, "Boot and startup failure", 3, "Boot timing", symptom_data.get("affected_activity_label") or symptom_data.get("affected_activity"))

        if _has(warning_text, ["black screen", "remains black", "screen goes black", "no boot", "no boot device", "startup repair"]):
            _add(score, evidence, "Boot and startup failure", 5, "Boot warning detail", symptom_data.get("warning_signs_label") or symptom_data.get("warning_signs"))

    if reported_category == "Display driver behavior":
        if _has(warning_text, ["flicker", "screen", "display driver", "visual", "glitch"]):
            _add(score, evidence, "Display driver behavior", 4, "Display warning detail", symptom_data.get("warning_signs_label") or symptom_data.get("warning_signs"))

    if _has(recent_changes_text, ["driver update"]) and reported_category in ["Driver conflict", "Display driver behavior"]:
        _add(score, evidence, reported_category, 2, "Recent driver change", symptom_data.get("recent_changes_label") or symptom_data.get("recent_changes"))

    if _has(recent_changes_text, ["windows update"]) and reported_category in ["OS performance degradation", "Boot and startup failure", "Driver conflict"]:
        _add(score, evidence, reported_category, 1, "Recent Windows update", symptom_data.get("recent_changes_label") or symptom_data.get("recent_changes"))

    if _has(recent_changes_text, ["hardware upgrade"]) and reported_category in ["Storage health behavior", "Boot and startup failure", "Thermal condition"]:
        _add(score, evidence, reported_category, 1, "Recent hardware change", symptom_data.get("recent_changes_label") or symptom_data.get("recent_changes"))

    ranked = sorted(score.items(), key=lambda item: item[1], reverse=True)
    diagnosed_category = ranked[0][0]
    top_score = ranked[0][1]
    second_score = ranked[1][1]

    if not objective_problem_found and not strong_user_warning and top_score < 5:
        diagnosed_category = "No active issue detected"
        action_category = "Monitor"
        confidence_label = "High"
        evidence_for_result = []
        ai_explanation = _build_no_issue_explanation(proof)
    else:
        if top_score >= 9 and top_score - second_score >= 2:
            confidence_label = "High"
        elif top_score >= 5:
            confidence_label = "Moderate"
        else:
            confidence_label = "Low"

        if top_score < 4 and not strong_user_warning:
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
            severity == "high"
            or _has(warning_text, ["no display", "black screen", "no boot device", "startup repair"])
            or _flag(system_state_signals, "mentions_black_screen")
            or _flag(system_state_signals, "mentions_no_boot")
        ):
            action_category = "Escalate for Professional Inspection"
            confidence_label = "High"

        evidence_for_result = [item for item in evidence if item["category"] == diagnosed_category][:6]

        if diagnosed_category == "No active issue detected":
            ai_explanation = _build_no_issue_explanation(proof)
        else:
            ai_explanation = _build_plain_explanation(diagnosed_category, action_category, confidence_label, evidence_for_result, proof)

    display_proof = _select_display_proof(diagnosed_category, proof, evidence_for_result)

    if client and diagnosed_category != "No active issue detected":
        prompt = f"""
Explain this RigMD diagnostic result in one short, clear paragraph for a non-technical Windows desktop user.

Rules:
- Do not use markdown, bullets, or numbered lists.
- Do not say the diagnosis is guaranteed.
- Do not invent causes, parts, tools, readings, temperatures, or events.
- Use only the result, evidence, proof shown to the user, and user report shown below.
- Do not describe normal or observed proof items as active problems.
- Mention live scan data only when it supports the result.
- End with the safest next step.

Result: {diagnosed_category}
Action: {action_category}
Confidence: {confidence_label}
Evidence: {evidence_for_result}
Proof shown to the user: {display_proof}
User report: {symptom_data}
"""
        try:
            response = client.models.generate_content(model="gemini-2.5-flash", contents=prompt)
            ai_text = _clean_ai_text(response.text)
            if ai_text:
                ai_explanation = ai_text
        except Exception:
            pass

    verification_target = _get_verification_target(diagnosed_category, proof, evidence_for_result)
    recommended_next_step = _get_recommended_next_step(diagnosed_category, action_category, proof, evidence_for_result)

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
        "proof": display_proof,
        "all_live_proof": proof,
        "verification_target": verification_target,
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
        "recommended_next_step": recommended_next_step,
        "recommendations": [],
        "is_recurring": False,
        "created_at": datetime.now().isoformat(),
    }