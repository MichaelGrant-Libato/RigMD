from dotenv import load_dotenv
import os

load_dotenv()

DATABASE_URL = os.getenv("DATABASE_URL")
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY")
FRONTEND_URL = os.getenv("FRONTEND_URL", "http://localhost:5173")

if not DATABASE_URL:
    raise ValueError("DATABASE_URL is not set in your .env file")

if not GEMINI_API_KEY:
    raise ValueError("GEMINI_API_KEY is not set in your .env file")