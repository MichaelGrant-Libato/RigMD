from sqlalchemy import Column, String, Text, DateTime, ForeignKey, Boolean
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
import uuid
 
from backend.database import Base


class Session(Base):
    """
    Stores each diagnostic session with symptom intake data
    and the resulting diagnosis.
    """
    __tablename__ = "sessions"

    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    profile_id = Column(UUID(as_uuid=True), ForeignKey("profiles.id"), nullable=False)
    
    # Symptom intake data (8 data points)
    symptom_type = Column(String, nullable=False)
    affected_activity = Column(String, nullable=True)
    frequency = Column(String, nullable=False)
    severity = Column(String, nullable=False)
    duration = Column(String, nullable=True)
    recent_changes = Column(Text, nullable=True)
    system_state = Column(String, nullable=True)
    warning_signs = Column(Text, nullable=True)
    
    # Diagnostic results
    diagnosed_category = Column(String, nullable=False)
    action_category = Column(String, nullable=False)
    confidence_label = Column(String, nullable=False)
    ai_explanation = Column(Text, nullable=True)
    
    is_recurring = Column(Boolean, default=False)
    created_at = Column(DateTime(timezone=True), server_default=func.now())

    # Relationships
    profile = relationship("Profile", back_populates="sessions")
    recommendations = relationship("Recommendation", back_populates="session")
 
 
class Recommendation(Base):
    """
    Stores each row of the warning signs reference table
    generated after each diagnostic session result.
    """
    __tablename__ = "recommendations"
 
    id = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    session_id = Column(UUID(as_uuid=True), ForeignKey("sessions.id"), nullable=False)
    warning_sign = Column(String, nullable=False)
    threshold = Column(String, nullable=False)
    recommended_action = Column(Text, nullable=False)
    created_at = Column(DateTime(timezone=True), server_default=func.now())
 
    session = relationship("Session", back_populates="recommendations")
 