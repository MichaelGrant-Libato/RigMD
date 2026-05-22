from pydantic import BaseModel
from uuid import UUID
from datetime import datetime
from typing import Optional


class ProfileCreate(BaseModel):
    """What the frontend sends when creating a profile."""
    cpu_model: str
    ram_capacity: str
    storage_type: str
    storage_capacity: str
    os_version: str
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
    os_version: str
    gpu_driver: Optional[str]
    chipset_driver: Optional[str]
    system_age: Optional[str]
    created_at: datetime

    class Config:
        from_attributes = True