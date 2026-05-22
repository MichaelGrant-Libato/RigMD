from sqlalchemy import Column, String, Text, DateTime, ForeignKey
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
import uuid
 
from backend.database import Base
 
 
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
 