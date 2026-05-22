from sqlalchemy import Column, String, DateTime
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
import uuid

from backend.database import Base


class Profile(Base):
    """
    Stores the user's desktop PC specifications.
    Every diagnostic session is linked to a profile.
    """
    __tablename__ = "profiles"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    cpu_model = Column(String, nullable=False)
    ram_capacity = Column(String, nullable=False)
    storage_type = Column(String, nullable=False)       # e.g. SSD, HDD, NVMe
    storage_capacity = Column(String, nullable=False)   # e.g. 512GB
    os_version = Column(String, nullable=False)         # e.g. Windows 11 23H2
    gpu_driver = Column(String, nullable=True)          # e.g. 556.12
    chipset_driver = Column(String, nullable=True)
    system_age = Column(String, nullable=True)          # e.g. 2 years
    created_at = Column(DateTime(timezone=True), server_default=func.now())

    # One profile → many sessions
    sessions = relationship("Session", back_populates="profile")