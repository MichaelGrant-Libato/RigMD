from pydantic import BaseModel
from uuid import UUID
from typing import Optional, List


class DiagnosisRequest(BaseModel):
    """
    What the frontend sends when the user submits the symptom intake form.
    Contains the profile ID + all 8 symptom data points.
    """
    profile_id: UUID
    symptom_type: str
    affected_activity: str
    frequency: str
    severity: str
    duration: str
    recent_changes: Optional[str] = None
    system_state: str
    warning_signs: Optional[str] = None


class WarningSignRow(BaseModel):
    """One row of the warning signs reference table."""
    warning_sign: str
    threshold: str
    recommended_action: str


class DiagnosisResponse(BaseModel):
    """
    What the backend sends back after running the diagnostic engine.
    This is what the ResultPage displays.
    """
    session_id: UUID
    diagnosed_category: str
    action_category: str
    confidence_label: str
    ai_explanation: str
    is_recurring: bool
    warning_signs_table: List[WarningSignRow]