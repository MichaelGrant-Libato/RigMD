from dotenv import load_dotenv
import os
import warnings

load_dotenv()

DATABASE_URL = os.getenv("DATABASE_URL")


GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
FRONTEND_URL = os.getenv("FRONTEND_URL", "http://localhost:5173")

# Check if DATABASE_URL is set and valid
if not DATABASE_URL or "[YOUR-PASSWORD]" in DATABASE_URL:
    warnings.warn(
        "DATABASE_URL is not properly configured in your .env file. "
        "Database features will not work until you set a valid connection string.",
        UserWarning
    )
    # Provide a dummy URL to prevent crashes during development
    DATABASE_URL = "postgresql://user:pass@localhost:5432/dummy"

if not GEMINI_API_KEY:
    warnings.warn(
        "GEMINI_API_KEY is not set in your .env file. "
        "AI explanation features will not work.",
        UserWarning
    )