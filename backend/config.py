import os
import warnings
from pathlib import Path

from dotenv import load_dotenv


BACKEND_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = BACKEND_DIR.parent

# The README instructs developers to keep backend settings in backend/.env
# while running uvicorn from the project root.
load_dotenv(PROJECT_ROOT / ".env")
load_dotenv(BACKEND_DIR / ".env", override=True)

DATABASE_URL = os.getenv("DATABASE_URL")
DATABASE_CONFIGURED = bool(DATABASE_URL and "[YOUR-PASSWORD]" not in DATABASE_URL)
DATABASE_CONFIG_ERROR = None


GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
FRONTEND_URL = os.getenv("FRONTEND_URL", "http://localhost:5173")

# Check if DATABASE_URL is set and valid
if not DATABASE_CONFIGURED:
    DATABASE_CONFIG_ERROR = (
        "DATABASE_URL is not properly configured in backend/.env. "
        "Set it to the Supabase PostgreSQL connection string from the README."
    )
    warnings.warn(
        "DATABASE_URL is not properly configured in backend/.env. "
        "Database features will not work until you set a valid connection string.",
        UserWarning
    )

if not GEMINI_API_KEY:
    warnings.warn(
        "GEMINI_API_KEY is not set in your .env file. "
        "AI explanation features will not work.",
        UserWarning
    )
