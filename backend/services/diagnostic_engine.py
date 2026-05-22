"""
diagnostic_engine.py
--------------------
Rule-based engine that maps the 8 symptom intake data points
+ system profile context to a probable internal cause category.

Primary categories:
  - OS performance degradation
  - Driver conflict
  - Storage health behavior
  - Boot and startup failure

Supporting categories:
  - Thermal condition
  - Display driver behavior
  - System log event flags
"""


def run_diagnostic(symptom_data: dict, profile_data: dict) -> dict:
    """
    Accepts symptom intake data and system profile, returns:
      - diagnosed_category: str
      - action_category:    str  (Monitor / Maintain / Troubleshoot / Escalate)
      - confidence_label:   str  (High / Moderate / Low)

    All logic here is rule-based. Gemini API is called separately
    in ai_explainer.py to generate the plain-language explanation.
    """
    symptom_type = symptom_data.get("symptom_type", "").lower()
    warning_signs = symptom_data.get("warning_signs", "") or ""
    recent_changes = symptom_data.get("recent_changes", "") or ""
    frequency = symptom_data.get("frequency", "").lower()
    severity = symptom_data.get("severity", "").lower()
    affected_activity = symptom_data.get("affected_activity", "").lower()

    diagnosed_category = "OS performance degradation"
    action_category = "Monitor"
    confidence_label = "Moderate"

    # ── BOOT AND STARTUP FAILURE ──────────────────────────────────────────
    if symptom_type in ["boot and startup issues", "boot failure"]:
        diagnosed_category = "Boot and startup failure"
        if "startup repair" in warning_signs.lower() or "no display" in warning_signs.lower():
            action_category = "Escalate for Professional Inspection"
            confidence_label = "High"
        elif severity == "high":
            action_category = "Troubleshoot"
            confidence_label = "High"
        else:
            action_category = "Troubleshoot"
            confidence_label = "Moderate"

    # ── DRIVER CONFLICT ───────────────────────────────────────────────────
    elif symptom_type in ["driver-related issues", "driver conflict"]:
        diagnosed_category = "Driver conflict"
        if recent_changes and ("driver" in recent_changes.lower() or "update" in recent_changes.lower()):
            action_category = "Troubleshoot"
            confidence_label = "High"
        elif "yellow flag" in warning_signs.lower() or "device manager" in warning_signs.lower():
            action_category = "Troubleshoot"
            confidence_label = "High"
        else:
            action_category = "Troubleshoot"
            confidence_label = "Moderate"

    # ── STORAGE HEALTH ────────────────────────────────────────────────────
    elif symptom_type in ["storage os-level issues", "storage health"]:
        diagnosed_category = "Storage health behavior"
        if "caution" in warning_signs.lower() or "bad" in warning_signs.lower() or "smart" in warning_signs.lower():
            action_category = "Maintain"
            confidence_label = "High"
        elif severity == "high":
            action_category = "Troubleshoot"
            confidence_label = "Moderate"
        else:
            action_category = "Monitor"
            confidence_label = "Moderate"

    # ── DISPLAY / RENDERING ───────────────────────────────────────────────
    elif symptom_type in ["display and rendering issues"]:
        diagnosed_category = "Display driver behavior"
        if "no display" in warning_signs.lower():
            action_category = "Escalate for Professional Inspection"
            confidence_label = "High"
        elif recent_changes and "driver" in recent_changes.lower():
            action_category = "Troubleshoot"
            confidence_label = "High"
        else:
            action_category = "Troubleshoot"
            confidence_label = "Moderate"

    # ── THERMAL CONDITION ─────────────────────────────────────────────────
    elif symptom_type in ["thermal condition", "overheating"]:
        diagnosed_category = "Thermal condition"
        if affected_activity in ["gaming", "heavy computation", "video editing"]:
            action_category = "Maintain"
            confidence_label = "High"
        else:
            action_category = "Maintain"
            confidence_label = "Moderate"

    # ── SYSTEM STABILITY / OS PERFORMANCE ────────────────────────────────
    elif symptom_type in ["os performance issues", "system stability issues"]:
        diagnosed_category = "OS performance degradation"
        if frequency == "always" and severity == "high":
            action_category = "Troubleshoot"
            confidence_label = "High"
        elif "bsod" in warning_signs.lower() or "blue screen" in warning_signs.lower():
            diagnosed_category = "Driver conflict"
            action_category = "Troubleshoot"
            confidence_label = "High"
        elif recent_changes:
            action_category = "Troubleshoot"
            confidence_label = "Moderate"
        else:
            action_category = "Monitor"
            confidence_label = "Low"

    # ── NO POWER / UNRESPONSIVE ───────────────────────────────────────────
    elif symptom_type in ["no-power or unresponsive system triage"]:
        diagnosed_category = "Boot and startup failure"
        action_category = "Escalate for Professional Inspection"
        confidence_label = "High"

    elif symptom_type in ["system log event flags"]:
        diagnosed_category = "System log event flags"
        if "critical" in warning_signs.lower():
            action_category = "Troubleshoot"
            confidence_label = "High"
        else:
            action_category = "Monitor"
            confidence_label = "Moderate"

    return {
        "diagnosed_category": diagnosed_category,
        "action_category": action_category,
        "confidence_label": confidence_label,
    }