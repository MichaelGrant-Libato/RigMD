# test.py
from backend.services.diagnosis_engine import run_diagnostic

# 1. Create a fake symptom report that SHOULD trigger a specific rule
mock_symptoms = {
    "symptom_type": "boot and startup issues",
    "affected_activity": "startup",
    "frequency": "always",
    "severity": "high",
    "system_state": "pressing power button",
    "warning_signs": "startup repair loop on screen",
    "recent_changes": ""
}

# 2. Create a fake profile (the engine doesn't strictly use this yet, but requires the argument)
mock_profile = {
    "os_version": "Windows 11",
    "cpu_model": "Intel Core i5"
}

# 3. Run the engine
result = run_diagnostic(mock_symptoms, mock_profile)

# 4. Print the result
print("\n--- DIAGNOSTIC RESULT ---")
print(f"Category:   {result['diagnosed_category']}")
print(f"Action:     {result['action_category']}")
print(f"Confidence: {result['confidence_label']}")
print("-------------------------\n")