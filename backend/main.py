from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from backend.services.diagnostic_engine import run_diagnostic
from backend.routers import hardware 

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["http://localhost:5173"], 
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Attach the live hardware route
app.include_router(hardware.router)

@app.get("/")
def read_root():
    return {"message": "Backend is running!"}

@app.post("/api/diagnose/")
def run_diagnostic_endpoint(payload: dict):
    result = run_diagnostic(symptom_data=payload, profile_data={})
    return result