"""
ai_explainer.py
---------------
Calls the Gemini API with a constrained structured prompt to generate
a plain-language explanation for a diagnostic result.

Rules (from SRS):
  - Only structured symptom data, profile context, and classified result
    are sent to the API. NO personally identifiable information.
  - If the API is unavailable, a fallback template explanation is returned.
  - All API communication uses HTTPS (enforced by the SDK).
"""

import os
import logging

import google.generativeai as genai

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "")
GEMINI_MODEL = "gemini-1.5-flash"  

if GEMINI_API_KEY:
    genai.configure(api_key=GEMINI_API_KEY)
else:
    logger.warning("GEMINI_API_KEY is not set. AI explanations will use fallback text.")


# ---------------------------------------------------------------------------
# Fallback templates (used when the API is unavailable)
# ---------------------------------------------------------------------------

FALLBACK_TEMPLATES: dict[str, str] = {
    "OS performance degradation": (
        "Your PC is showing signs of OS-level performance degradation. "
        "This is commonly caused by accumulated software processes, startup overload, "
        "or fragmented system files slowing down normal operations. "
        "Running standard maintenance steps such as disk cleanup and startup management "
        "should help improve performance."
    ),
    "Driver conflict": (
        "A driver conflict has been detected based on your reported symptoms. "
        "This typically occurs after a recent driver or Windows update that introduces "
        "an incompatibility with your hardware. Updating or rolling back the affected "
        "driver through Device Manager is the recommended first step."
    ),
    "Storage health behavior": (
        "Your reported symptoms suggest a storage-level issue on your desktop PC. "
        "This may involve the file system, boot partition, or early signs of drive wear. "
        "Checking your drive health using Windows tools and backing up important files "
        "is strongly advised before attempting other steps."
    ),
    "Boot and startup failure": (
        "Your PC is experiencing boot or startup-related issues. "
        "This can be caused by corrupted startup files, a failed Windows update, "
        "or a hardware component affecting the POST sequence. "
        "If Windows Startup Repair does not resolve the issue, professional inspection "
        "may be required."
    ),
    "Thermal condition": (
        "Your system appears to be running under elevated thermal conditions. "
        "This is often caused by dust accumulation in cooling components, "
        "dried thermal paste on the processor, or inadequate airflow inside the case. "
        "Cleaning the cooling system and reapplying thermal compound are the recommended actions."
    ),
    "Display driver behavior": (
        "A display or rendering issue has been identified, likely related to your GPU driver. "
        "This can occur after a driver update or a configuration change affecting the display output. "
        "Reinstalling or rolling back the GPU driver through Device Manager is the first step to try."
    ),
    "System log event flags": (
        "Your system has logged observable event-level indicators that match your reported symptoms. "
        "These flags can point to software crashes, failed services, or hardware events "
        "logged by Windows. Reviewing the Event Viewer for critical entries will help "
        "identify the root cause."
    ),
}

DEFAULT_FALLBACK = (
    "Based on your reported symptoms and system profile, RigMD has identified a probable "
    "internal cause for the issue you are experiencing. Please follow the recommended next steps "
    "listed below to address the problem. If symptoms persist or worsen, consider escalating "
    "to a qualified technician."
)


# ---------------------------------------------------------------------------
# Prompt builder
# ---------------------------------------------------------------------------

def _build_prompt(
    symptom_data: dict,
    profile_data: dict,
    diagnosis: dict,
) -> str:
    """
    Constructs the constrained prompt sent to Gemini.
    Only structured, non-PII fields are included.
    """

    symptom_type      = symptom_data.get("symptom_type", "unspecified")
    affected_activity = symptom_data.get("affected_activity", "general use")
    frequency         = symptom_data.get("frequency", "unspecified")
    severity          = symptom_data.get("severity", "unspecified")
    duration          = symptom_data.get("duration", "unspecified")
    recent_changes    = symptom_data.get("recent_changes") or "none reported"
    warning_signs     = symptom_data.get("warning_signs") or "none reported"
    system_state      = symptom_data.get("system_state") or "not specified"

    cpu_model      = profile_data.get("cpu_model", "unspecified")
    ram_capacity   = profile_data.get("ram_capacity", "unspecified")
    storage_type   = profile_data.get("storage_type", "unspecified")
    os_version     = profile_data.get("os_version", "unspecified")
    gpu_driver     = profile_data.get("gpu_driver") or "unspecified"
    chipset_driver = profile_data.get("chipset_driver") or "unspecified"
    system_age     = profile_data.get("system_age") or "unspecified"

    diagnosed_category = diagnosis.get("diagnosed_category", "unspecified")
    action_category    = diagnosis.get("action_category", "unspecified")
    confidence_label   = diagnosis.get("confidence_label", "unspecified")

    return f"""
You are the diagnostic explanation engine for RigMD, a symptom-guided PC diagnostic tool.
Your only job is to write a plain-language explanation for the diagnostic result below.

RULES:
- Write 2 to 4 short paragraphs in plain English.
- Do NOT mention brand names, specific prices, or shopping recommendations.
- Do NOT recommend component replacement or hardware upgrades.
- Do NOT claim this is a confirmed diagnosis. Use words like "likely", "probable", "suggests".
- Do NOT use bullet points, headers, or markdown formatting.
- Write for a non-technical user who has never opened a PC before.
- End by telling the user what their next step should be based on the action category.

SYSTEM PROFILE:
- CPU: {cpu_model}
- RAM: {ram_capacity}
- Storage type: {storage_type}
- Operating system: {os_version}
- GPU driver version: {gpu_driver}
- Chipset driver version: {chipset_driver}
- System age: {system_age}

REPORTED SYMPTOM DATA:
- Symptom type: {symptom_type}
- Affected activity: {affected_activity}
- Frequency: {frequency}
- Severity: {severity}
- Duration: {duration}
- Recent changes made: {recent_changes}
- Warning signs observed: {warning_signs}
- System state: {system_state}

DIAGNOSTIC RESULT:
- Probable internal cause: {diagnosed_category}
- Recommended action category: {action_category}
- Confidence level: {confidence_label}

Write the plain-language explanation now:
""".strip()


# ---------------------------------------------------------------------------
# Public function
# ---------------------------------------------------------------------------

def generate_explanation(
    symptom_data: dict,
    profile_data: dict,
    diagnosis: dict,
) -> str:
    """
    Returns a plain-language explanation string.

    - Tries the Gemini API first.
    - Falls back to a pre-written template if the API is unavailable or errors.
    - Never raises an exception to the caller.
    """
    if not GEMINI_API_KEY:
        logger.info("No API key set — using fallback explanation.")
        return _get_fallback(diagnosis)

    try:
        model = genai.GenerativeModel(GEMINI_MODEL)
        prompt = _build_prompt(symptom_data, profile_data, diagnosis)

        response = model.generate_content(
            prompt,
            generation_config=genai.types.GenerationConfig(
                temperature=0.3,        # low temp = consistent, factual output
                max_output_tokens=400,  # keeps explanations concise
            ),
        )

        explanation = response.text.strip()

        if not explanation:
            logger.warning("Gemini returned empty response — using fallback.")
            return _get_fallback(diagnosis)

        logger.info("Gemini explanation generated successfully.")
        return explanation

    except Exception as exc:
        logger.error(f"Gemini API error: {exc} — using fallback explanation.")
        return _get_fallback(diagnosis)


def _get_fallback(diagnosis: dict) -> str:
    category = diagnosis.get("diagnosed_category", "")
    return FALLBACK_TEMPLATES.get(category, DEFAULT_FALLBACK)