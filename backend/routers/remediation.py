# backend/routers/remediation.py

from fastapi import APIRouter, HTTPException
from pydantic import BaseModel
from backend.services.remediation_service import get_available_actions, execute_action

router = APIRouter(prefix="/api/remediation", tags=["remediation"])


class ExecutePayload(BaseModel):
    action_id: str


@router.get("/actions")
def get_actions(category: str):
    """
    Returns the list of available Fix Now buttons for a diagnosed category.
    Called by DiagnosticResultView after a result is returned.

    Example: GET /api/remediation/actions?category=OS+performance+degradation
    """
    if not category:
        raise HTTPException(status_code=400, detail="category is required")

    actions = get_available_actions(category)
    return {"category": category, "actions": actions}


@router.post("/execute")
def run_action(payload: ExecutePayload):
    """
    Executes a single safe remediation action by its ID.
    Called when the user clicks a Fix Now button.

    Example: POST /api/remediation/execute  { "action_id": "clear_user_temp_files" }
    """
    if not payload.action_id:
        raise HTTPException(status_code=400, detail="action_id is required")

    result = execute_action(payload.action_id)

    if not result.get("success"):
        return result

    return result