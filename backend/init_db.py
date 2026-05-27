import sys
import os

# Append your directory path context so Python finds the backend package accurately
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from backend.database import engine, Base

# ─── CRITICAL FIX: IMPORT ALL MODELS HERE ────────────────────────────────────
# We must import your profile/user models so SQLAlchemy builds them first!
# (Adjust the path below if your Profile class lives in a different model file)
try:
    from backend.models.profile_model import Profile 
except ImportError:
    # If it's named something else or located elsewhere, import it here:
    pass

from backend.models.session_model import Session, Recommendation 
# ─────────────────────────────────────────────────────────────────────────────

print("Connecting to Supabase and initializing table architectures...")
try:
    Base.metadata.create_all(bind=engine)
    print("Successfully created all tables in Supabase!")
except Exception as e:
    print(f"Failed to initialize database tables: {e}")