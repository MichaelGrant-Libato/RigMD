from fastapi import APIRouter, HTTPException
from pydantic import BaseModel
from backend.services.remediation_service import get_available_actions, execute_action, open_target

router = APIRouter(prefix="/api/remediation", tags=["remediation"])


class ExecutePayload(BaseModel):
    action_id: str


class OpenTargetPayload(BaseModel):
    target: str


@router.get("/actions")
def get_actions(category: str):
    if not category:
        raise HTTPException(status_code=400, detail="category is required")

    actions = get_available_actions(category)
    return {"category": category, "actions": actions}


@router.post("/execute")
def run_action(payload: ExecutePayload):
    if not payload.action_id:
        raise HTTPException(status_code=400, detail="action_id is required")

    return execute_action(payload.action_id)


@router.post("/open-target")
def open_verification_target(payload: OpenTargetPayload):
    if not payload.target:
        raise HTTPException(status_code=400, detail="target is required")

    return open_target(payload.target)