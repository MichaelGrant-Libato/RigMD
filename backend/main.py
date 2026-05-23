from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from backend.config import FRONTEND_URL
from backend.services.diagnostic_engine import run_diagnostic
from backend.routers import hardware
from backend.routers import dashboard
from backend.routers import history
from backend.routers import recurring
from backend.routers import warning_signs

app = FastAPI(
    title="RigMD Backend",
    description="Backend API for RigMD diagnostic support system",
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        FRONTEND_URL,
        "http://localhost:5173",
        "http://127.0.0.1:5173",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(hardware.router)
app.include_router(dashboard.router)
app.include_router(history.router)
app.include_router(recurring.router)
app.include_router(warning_signs.router)


@app.get("/")
def read_root():
    return {"message": "Backend is running!"}


@app.post("/api/diagnose/")
def run_diagnostic_endpoint(payload: dict):
    result = run_diagnostic(symptom_data=payload, profile_data={})
    return result