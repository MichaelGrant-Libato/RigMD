# RigMD — PC Diagnostic Decision Support System

> A web application that interprets user-reported PC performance symptoms against a system profile to deliver action-classified diagnostic advisory output.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 18, Vite, Tailwind CSS, React Router v6, Axios |
| Backend | Python 3.11+, FastAPI, Uvicorn, Gemini API |
| Database | PostgreSQL via Supabase, SQLAlchemy, Alembic |

---

## Architecture

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
├── id              UUID PRIMARY KEY DEFAULT gen_random_uuid()
├── cpu_model       VARCHAR
├── ram_capacity    VARCHAR
├── storage_type    VARCHAR
├── storage_capacity VARCHAR
├── os_version      VARCHAR
├── gpu_driver      VARCHAR
├── chipset_driver  VARCHAR
├── system_age      VARCHAR
└── created_at      TIMESTAMP DEFAULT now()

sessions
├── id              UUID PRIMARY KEY DEFAULT gen_random_uuid()
├── profile_id      UUID REFERENCES profiles(id)
├── symptom_type    VARCHAR
├── affected_activity VARCHAR
├── frequency       VARCHAR
├── severity        VARCHAR
├── duration        VARCHAR
├── recent_changes  TEXT
├── system_state    VARCHAR
├── warning_signs   TEXT
├── diagnosed_category VARCHAR
├── action_category VARCHAR
├── confidence_label VARCHAR
├── ai_explanation  TEXT
├── is_recurring    BOOLEAN DEFAULT false
└── created_at      TIMESTAMP DEFAULT now()

recommendations
├── id              UUID PRIMARY KEY DEFAULT gen_random_uuid()
├── session_id      UUID REFERENCES sessions(id)
├── warning_sign    VARCHAR
├── threshold       VARCHAR
├── recommended_action TEXT
└── created_at      TIMESTAMP DEFAULT now()
```

---

## Team Roles

| Member | Layer | Responsibilities |
|---|---|---|
| Maestrado, Ralph Keane | Frontend | ProfilePage, IntakePage, routing, AppContext |
| Labaya, Godwin | Frontend | ResultPage, HistoryPage, all reusable components |
| Ruperez, Raymart | Backend | FastAPI setup, all routers, Pydantic schemas |
| Libato, Michael Grant | Backend | diagnostic_engine, advisory_module, ai_explainer |
| Macansantos, Axcelson| Database | Supabase setup, SQLAlchemy models, repositories, Alembic migrations |

---

## Getting Started

### Prerequisites
- Node.js 18+ and npm
- Python 3.11+
- A Supabase account (free tier at supabase.com)

---

### 1. Supabase setup (database member)

1. Go to [supabase.com](https://supabase.com) → New project
2. Name it `rigmd` → set a strong database password → create
3. Go to **Project Settings → Database → Connection string → URI**
4. Copy the connection string — it looks like:
   ```
   postgresql://postgres:[PASSWORD]@db.[PROJECT-REF].supabase.co:5432/postgres
   ```
5. Share the connection string securely with the team (use a private Discord message — never commit it)

---

### 2. Clone the repository (everyone)

```bash
git clone https://github.com/YOUR_USERNAME/rigmd.git
cd rigmd
git checkout dev
git pull origin dev
git checkout -b feature/your-module-name
```

---

### 3. Backend setup

```bash
cd backend
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
```

Create `backend/.env` from the example:
```bash
copy .env.example .env
# then open .env and fill in your values
```

Run the backend:
```bash
uvicorn main:app --reload --port 8000
```

API docs: `http://localhost:8000/docs`

---

### 4. Run database migrations (database member)

```bash
cd backend
alembic upgrade head
```

This creates all tables in your Supabase PostgreSQL database automatically.

---

### 5. Frontend setup

```bash
cd frontend
npm install
npm run dev
```

App runs at: `http://localhost:5173`

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
| **Monitor** | Mild, inconsistent symptoms |
| **Maintain** | Thermal or software bloat indicators |
| **Troubleshoot** | Symptoms after a driver or system update |
| **Escalate for Professional Inspection** | Exceeds software-layer resolution |

---

## Branch Strategy

| Branch | Purpose |
|---|---|
| `main` | Stable only — merged from dev |
| `dev` | Active integration branch |
| `feature/[name]` | Per-member feature branches |

### Commit format
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
- The Supabase connection string contains your database password — treat it like an API key and never commit it.

---
