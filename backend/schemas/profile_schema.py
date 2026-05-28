from pydantic import BaseModel
from uuid import UUID
from datetime import datetime
from typing import Any, Optional


class ProfileCreate(BaseModel):
    """What the frontend sends when creating a profile."""
    cpu_model: str
    ram_capacity: str
    storage_type: str
    storage_capacity: str
    storage_details: Optional[list[dict[str, Any]]] = None
    os_version: str
    gpu_driver: Optional[str] = None
    chipset_driver: Optional[str] = None
    system_age: Optional[str] = None


class ProfileUpdate(BaseModel):
    """Fields the frontend may send when updating a saved profile."""
    cpu_model: Optional[str] = None
    ram_capacity: Optional[str] = None
    storage_type: Optional[str] = None
    storage_capacity: Optional[str] = None
    storage_details: Optional[list[dict[str, Any]]] = None
    os_version: Optional[str] = None
    gpu_driver: Optional[str] = None
    chipset_driver: Optional[str] = None
    system_age: Optional[str] = None


class ProfileResponse(BaseModel):
    """What the backend sends back after saving a profile."""
    id: UUID
    cpu_model: str
    ram_capacity: str
    storage_type: str
    storage_capacity: str
    storage_details: Optional[list[dict[str, Any]]] = None
    os_version: str
    gpu_driver: Optional[str]
    chipset_driver: Optional[str]
    system_age: Optional[str]
    created_at: datetime

    class Config:
        from_attributes = True
