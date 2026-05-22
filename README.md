# RigMD — PC Diagnostic Decision Support System

> A web application that interprets user-reported PC performance symptoms against a system profile to deliver action-classified diagnostic advisory output.

**Team:** 2526-sem2-it332-11 | **Course:** IT 332 | **AY 2025–2026, Semester 2**

---

## What is RigMD?

RigMD is a web application that helps non-technical desktop PC owners identify probable causes of Windows-observable performance symptoms and decide on a clearly classified next action — without needing a technical background to interpret the results.

---

## Features

- System Profile Module (manual entry)
- Structured OS Symptom Intake (guided session, 8+ data points)
- Internal Diagnostic Interpretation Engine (rule-based + AI-assisted)
- Advisory Action Module (Monitor / Maintain / Troubleshoot / Escalate)
- Transparent Result Dashboard with warning signs reference table
- Session History & Recurring Pattern Detection (PostgreSQL via Supabase)

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 18, Vite, Tailwind CSS, React Router v6, Axios |
| Backend | Python 3.11+, FastAPI, Uvicorn, Gemini API |
| Database | PostgreSQL via Supabase, SQLAlchemy, Alembic |

---

## System Architecture

```
React (localhost:5173)
    ↕ HTTP / REST API (Axios)
FastAPI (localhost:8000)
    ↕ SQLAlchemy ORM + psycopg2
PostgreSQL (Supabase cloud)
```

---

## Project Structure

```
rigmd/
│
├── frontend/                        # React + Vite + Tailwind
│   ├── public/
│   ├── src/
│   │   ├── api/
│   │   │   └── axiosClient.js       # Axios base config
│   │   ├── components/
│   │   │   ├── Navbar.jsx
│   │   │   ├── ActionBadge.jsx
│   │   │   ├── WarningTable.jsx
│   │   │   ├── ConfidenceBadge.jsx
│   │   │   └── SessionCard.jsx
│   │   ├── pages/
│   │   │   ├── ProfilePage.jsx
│   │   │   ├── IntakePage.jsx
│   │   │   ├── ResultPage.jsx
│   │   │   └── HistoryPage.jsx
│   │   ├── hooks/
│   │   │   ├── useProfile.js
│   │   │   └── useSession.js
│   │   ├── context/
│   │   │   └── AppContext.jsx
│   │   ├── App.jsx
│   │   └── main.jsx
│   ├── index.html
│   ├── vite.config.js
│   ├── tailwind.config.js
│   └── package.json
│
├── backend/                         # FastAPI + Uvicorn
│   ├── routers/
│   │   ├── __init__.py
│   │   ├── profile.py               # /api/profile
│   │   ├── session.py               # /api/session
│   │   ├── diagnose.py              # /api/diagnose
│   │   └── history.py               # /api/history
│   ├── services/
│   │   ├── __init__.py
│   │   ├── diagnostic_engine.py
│   │   ├── advisory_module.py
│   │   └── ai_explainer.py
│   ├── schemas/
│   │   ├── __init__.py
│   │   ├── profile_schema.py
│   │   ├── session_schema.py
│   │   └── diagnosis_schema.py
│   ├── main.py
│   ├── config.py
│   └── requirements.txt
│
├── database/                        # SQLAlchemy + Supabase PostgreSQL
│   ├── __init__.py
│   ├── database.py                  # Engine and session setup
│   ├── models/
│   │   ├── __init__.py
│   │   ├── profile_model.py
│   │   ├── session_model.py
│   │   └── recommendation_model.py
│   ├── repositories/
│   │   ├── __init__.py
│   │   ├── profile_repository.py
│   │   └── session_repository.py
│   └── migrations/                  # Alembic migration files
│
├── tests/
│   ├── test_diagnostic_engine.py
│   ├── test_advisory_module.py
│   └── scenarios/
│       └── scenario_01.json
│
├── .env.example
├── .gitignore
└── README.md
```

---

## Database Schema (Supabase / PostgreSQL)

```
profiles
├── id                UUID PRIMARY KEY DEFAULT gen_random_uuid()
├── cpu_model         VARCHAR
├── ram_capacity      VARCHAR
├── storage_type      VARCHAR
├── storage_capacity  VARCHAR
├── os_version        VARCHAR
├── gpu_driver        VARCHAR
├── chipset_driver    VARCHAR
├── system_age        VARCHAR
└── created_at        TIMESTAMP DEFAULT now()

sessions
├── id                UUID PRIMARY KEY DEFAULT gen_random_uuid()
├── profile_id        UUID REFERENCES profiles(id)
├── symptom_type      VARCHAR
├── affected_activity VARCHAR
├── frequency         VARCHAR
├── severity          VARCHAR
├── duration          VARCHAR
├── recent_changes    TEXT
├── system_state      VARCHAR
├── warning_signs     TEXT
├── diagnosed_category VARCHAR
├── action_category   VARCHAR
├── confidence_label  VARCHAR
├── ai_explanation    TEXT
├── is_recurring      BOOLEAN DEFAULT false
└── created_at        TIMESTAMP DEFAULT now()

recommendations
├── id                UUID PRIMARY KEY DEFAULT gen_random_uuid()
├── session_id        UUID REFERENCES sessions(id)
├── warning_sign      VARCHAR
├── threshold         VARCHAR
├── recommended_action TEXT
└── created_at        TIMESTAMP DEFAULT now()
```

---

## Team Members

| Name | GitHub | Responsibilities |
|---|---|---|
| Libato, Michael Grant | @michaelgrantlibato7@gmail.com | Backend — diagnostic_engine, advisory_module, ai_explainer |
| Macansantos, Axcelson | @axcelsonmacansantos@gmail.com | Database — Supabase setup, SQLAlchemy models, Alembic migrations |
| Ruperez, Raymart | @raymartruperez@gmail.com | Backend — FastAPI setup, all routers, Pydantic schemas |
| Maestrado, Ralph Keane | @maestradoralphkeane@gmail.com | Frontend — ProfilePage, IntakePage, routing, AppContext |
| Labaya, Godwin | @glabaya123@gmail.com | Frontend — ResultPage, HistoryPage, all reusable components |

---

## Getting Started

### Prerequisites

- Node.js 18+ and npm
- Python 3.11+
- Access to the team Discord (for environment variables)

---

### 1. Clone the repository (everyone)

```bash
git clone https://github.com/YOUR_USERNAME/rigmd.git
cd rigmd
git checkout main
git pull origin main
git checkout -b feature/your-module-name (optional if planned to create new branch/module)
```

---

### 2. Backend setup (everyone)

```bash
cd backend
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
```

Create your local `.env` file:

```bash
# If .env.example exists:
copy .env.example .env

# Otherwise create .env manually with:
# GEMINI_API_KEY=your_key_here
# DATABASE_URL=your_supabase_connection_string
# FRONTEND_URL=http://localhost:5173
```

> Open the `.env` file and paste in the `GEMINI_API_KEY` and `DATABASE_URL`.  
> Check the pinned messages in messenger for these credentials.

---

### 3. Database migrations (optional)

Once the SQLAlchemy models are written, make sure your backend virtual environment is active, then push the tables to Supabase:

```bash
cd backend
venv\Scripts\activate
alembic upgrade head
```

Verify the tables were created in **Supabase → Table Editor**.

---

### 4. Run the Backend

**Option A: Using activated virtual environment (recommended)**

```bash
# From backend folder with venv activated
cd backend
venv\Scripts\activate
cd ..
uvicorn backend.main:app --reload --port 8000
```

**Option B: Direct path to uvicorn**

```bash
# From project root (rigmd folder)
backend\venv\Scripts\uvicorn.exe backend.main:app --reload --port 8000
```

Backend runs at: `http://localhost:8000`  
API docs available at: `http://localhost:8000/docs`

---

### 5. Frontend setup (everyone)

Open a **second terminal window** (keep the backend running), then:

```bash
cd frontend
npm install
npm run dev
```

Frontend runs at: `http://localhost:5173`

---

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/profile` | Save system profile |
| GET | `/api/profile/{id}` | Get system profile |
| POST | `/api/diagnose` | Run diagnostic + return advisory output |
| POST | `/api/session` | Save completed session |
| GET | `/api/history` | Get all sessions |
| GET | `/api/history/{profile_id}` | Get sessions for a specific profile |

---

## Action Categories

| Category | When it applies |
|---|---|
| **Monitor** | Mild, inconsistent symptoms with no clear pattern yet |
| **Maintain** | Symptoms suggest thermal issues or software bloat |
| **Troubleshoot** | Symptoms followed a recent driver, software, or system update |
| **Escalate for Professional Inspection** | Conditions exceed software-layer resolution |

---

## Branch Strategy

| Branch | Purpose |
|---|---|
| `main` | Stable releases only — merged from dev after full review |
| `dev` | Active integration branch — all features merge here first |
| `feature/[name]` | Individual feature branches per member |

### Commit message format

```
feat: add intake form step validation
fix: correct advisory logic for boot failure
refactor: move diagnosis to service layer
docs: update API table in README
```

---

## Backend requirements.txt

```
fastapi==0.115.0
uvicorn==0.30.6
sqlalchemy==2.0.36
alembic==1.13.3
pydantic==2.9.2
python-dotenv==1.0.1
google-generativeai==0.8.3
psycopg2-binary==2.9.10
```

---

## Important Notes

- Scoped to functional or partially functional Windows desktop PCs only.
- Does not evaluate physical hardware or recommend component replacement.
- All advisory output is probabilistic, not deterministic.
- No user data is transmitted externally except the structured prompt sent to Gemini API.
- The Supabase connection string contains the live database password — treat it like an API key and never commit it to version control.

---
