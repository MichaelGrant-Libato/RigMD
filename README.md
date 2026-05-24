# RigMD — PC Diagnostic Decision Support System

> A Windows desktop PC diagnostic advisory system that detects hardware/software profile data, interprets user-reported symptoms, tracks diagnostic history, identifies recurring patterns, and provides action-classified advisory output.

**Team:** 2526-sem2-it332-11  
**Course:** IT 332  
**AY:** 2025–2026, Semester 2

---

## Team Members

| Name | GitHub / Contact | Responsibilities |
|---|---|---|
| Libato, Michael Grant | @michaelgrantlibato7@gmail.com | Backend — diagnostic engine, advisory module, AI explainer |
| Macansantos, Axelson | @axcelsonmacansantos@gmail.com | Database — Supabase setup, SQLAlchemy models, Alembic migrations |
| Ruperez, Raymart | @raymartruperez@gmail.com | Backend — FastAPI setup, all routers, Pydantic schemas |
| Maestrado, Ralph Keane | @maestradoralphkeane@gmail.com | Frontend — ProfilePage, IntakePage, routing, AppContext |
| Labaya, Godwin | @glabaya123@gmail.com | Frontend — ResultPage, HistoryPage, reusable components |

---
---

## What is RigMD?

RigMD is a diagnostic support system for desktop PC users who experience Windows-observable performance symptoms.

It helps users understand possible causes of PC issues by connecting:

- Live detected system profile data
- Structured symptom intake
- Diagnostic interpretation
- Recommended action category
- Diagnostic history
- Recurring pattern detection
- Warning signs reference

RigMD does not replace professional hardware inspection. It provides probable advisory output to help users decide the next action.

---

## Core Features

### Home Dashboard

- Shows live system profile summary
- Shows latest diagnostic status
- Shows current action status
- Shows recurring issue count
- Shows warning signs count
- Shows action category distribution
- Shows session frequency overview
- Provides quick actions for diagnosis and history

### System Profile

- Automatically detects desktop PC information
- Detects CPU, RAM, GPU, storage, OS, GPU driver, chipset, and system age
- Uses live hardware data from the FastAPI backend
- Allows hardware data refresh

### New Diagnosis

- Guided symptom intake workflow
- Collects structured symptom data
- Sends diagnosis request to backend diagnostic engine
- Saves completed sessions into the database

### Diagnostic History

- Displays saved diagnostic sessions
- Supports action category filters
- Supports search and sorting
- Opens a session detail panel
- Shows symptom, probable cause, action, confidence, warning signs, and recommended next step

### Recurring Patterns

- Detects repeated symptoms
- Detects repeated probable causes
- Shows recurring issue count
- Shows worsening trends
- Shows action escalation
- Shows total occurrences
- Displays pattern timeline

### Warning Signs

- Provides a reference guide for observable PC warning indicators
- Shows warning sign meaning, threshold, category, and recommended action
- Highlights warning signs observed in saved sessions
- Supports category filters, search, and observed-only mode

### Reports

- Placeholder for technician-ready diagnostic report output
- Intended to summarize saved diagnostic sessions for review or repair consultation

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React, Vite, TypeScript, Tailwind CSS, Axios, Lucide React |
| Backend | Python, FastAPI, Uvicorn |
| Database | PostgreSQL via Supabase |
| ORM | SQLAlchemy |
| Environment Config | python-dotenv, Vite environment variables |
| Hardware Detection | WMI, pywin32, psutil |
| AI Support | Gemini API |

---

## System Architecture

```txt
React + Vite Frontend
http://localhost:5173
        |
        | Axios HTTP Requests
        v
FastAPI Backend
http://localhost:8000
        |
        | SQLAlchemy ORM
        v
Supabase PostgreSQL Database
```

Hardware detection works through the backend using Windows system APIs and local machine telemetry.

---

## Project Structure

```txt
RigMD/
│
├── backend/
│   ├── models/
│   │   ├── profile_model.py
│   │   ├── recommendation_model.py
│   │   └── session_model.py
│   │
│   ├── routers/
│   │   ├── dashboard.py
│   │   ├── hardware.py
│   │   ├── history.py
│   │   ├── recurring.py
│   │   └── warning_signs.py
│   │
│   ├── schemas/
│   │   ├── diagnosis_schema.py
│   │   └── profile_schema.py
│   │
│   ├── services/
│   │   └── diagnostic_engine.py
│   │
│   ├── config.py
│   ├── database.py
│   ├── main.py
│   ├── requirements.txt
│   ├── test.py
│   └── test_wmi.py
│
├── frontend/
│   ├── public/
│   │   ├── favicon.svg
│   │   └── icons.svg
│   │
│   ├── src/
│   │   ├── assets/
│   │   │   └── hero.png
│   │   │
│   │   ├── components/
│   │   │   ├── AppSidebar.tsx
│   │   │   └── TopHeader.tsx
│   │   │
│   │   ├── pages/
│   │   │   ├── DiagnosticHistoryView.tsx
│   │   │   ├── HardwareDashboard.tsx
│   │   │   ├── RecurringPatternsView.tsx
│   │   │   ├── ReportsView.tsx
│   │   │   ├── SystemProfileView.tsx
│   │   │   └── WarningSignsView.tsx
│   │   │
│   │   ├── types/
│   │   │   └── rigmd.ts
│   │   │
│   │   ├── App.css
│   │   ├── App.jsx
│   │   ├── index.css
│   │   ├── main.jsx
│   │   └── vite-env.d.ts
│   │
│   ├── .env
│   ├── .gitignore
│   ├── eslint.config.js
│   ├── index.html
│   ├── package.json
│   ├── package-lock.json
│   ├── postcss.config.js
│   ├── tailwind.config.js
│   └── vite.config.js
│
├── .gitignore
└── README.md
```

---

## Database Schema

RigMD uses three main tables.

### profiles

Stores the detected or saved desktop PC profile.

```txt
profiles
├── id
├── cpu_model
├── ram_capacity
├── storage_type
├── storage_capacity
├── os_version
├── gpu_driver
├── chipset_driver
├── system_age
└── created_at
```

### sessions

Stores each completed diagnostic session.

```txt
sessions
├── id
├── profile_id
├── symptom_type
├── affected_activity
├── frequency
├── severity
├── duration
├── recent_changes
├── system_state
├── warning_signs
├── diagnosed_category
├── action_category
├── confidence_label
├── ai_explanation
├── is_recurring
└── created_at
```

### recommendations

Stores warning sign rows and recommended actions connected to a diagnostic session.

```txt
recommendations
├── id
├── session_id
├── warning_sign
├── threshold
├── recommended_action
└── created_at
```

---

## Getting Started

### Prerequisites

Install these first:

- Node.js 18+
- npm
- Python 3.11+
- Git
- Supabase database access
- Gemini API key
- Windows 10/11 for live hardware detection

---

## 1. Clone the Repository

```bash
git clone https://github.com/YOUR_USERNAME/rigmd.git
cd RigMD
git checkout main
git pull origin main
```

Optional feature branch:

```bash
git switch -c feature/your-feature-name
```

---

## 2. Backend Setup

Run these from the project root:

```bash
cd backend
python -m venv venv
venv\Scripts\activate
pip install -r requirements.txt
```

If hardware detection packages are missing, install them:

```bash
pip install WMI pywin32 psutil
```

---

## 3. Backend Environment Setup

Create this file:

```txt
backend/.env
```

Add:

```env
GEMINI_API_KEY=your_gemini_api_key_here
DATABASE_URL=your_supabase_postgresql_connection_string_here
FRONTEND_URL=http://localhost:5173
```

Example Supabase connection format:

```env
DATABASE_URL=postgresql://postgres.YOUR_PROJECT_ID:YOUR_PASSWORD@aws-1-ap-southeast-2.pooler.supabase.com:5432/postgres
```

Important notes:

- Do not leave `[YOUR-PASSWORD]` in the connection string.
- Replace it with the real Supabase database password.
- If your password has special characters, URL-encode it.
- Never commit `.env` files.

---

## 4. Run the Backend

Always run the backend from the project root.

```bash
cd D:\RigMD
backend\venv\Scripts\python.exe -m uvicorn backend.main:app --reload --port 8000
```

Backend runs at:

```txt
http://localhost:8000
```

API documentation:

```txt
http://localhost:8000/docs
```

Test backend root:

```txt
http://localhost:8000/
```

Expected response:

```json
{
  "message": "Backend is running!"
}
```

---

## 5. Frontend Setup

Open a second terminal.

```bash
cd frontend
npm install
```

---

## 6. Frontend Environment Setup

Create this file:

```txt
frontend/.env
```

Add:

```env
VITE_API_BASE_URL=http://localhost:8000
```

This lets the frontend call the FastAPI backend.

---

## 7. Run the Frontend

```bash
cd frontend
npm run dev
```

Frontend runs at:

```txt
http://localhost:5173
```

---

## 8. Recommended Run Order

Start backend first:

```bash
cd D:\RigMD
backend\venv\Scripts\python.exe -m uvicorn backend.main:app --reload --port 8000
```

Then start frontend in another terminal:

```bash
cd D:\RigMD\frontend
npm run dev
```

---

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| GET | `/` | Backend health check |
| GET | `/api/hardware/live` | Get live hardware/system telemetry |
| POST | `/api/hardware/refresh` | Refresh hardware cache |
| GET | `/api/dashboard/summary` | Get home dashboard summary |
| POST | `/api/diagnose/` | Run diagnostic engine |
| GET | `/api/history/sessions` | Get diagnostic history sessions |
| GET | `/api/history/sessions/{session_id}` | Get one diagnostic session detail |
| GET | `/api/recurring/patterns` | Get detected recurring patterns |
| GET | `/api/recurring/patterns/{pattern_id}` | Get recurring pattern detail |
| GET | `/api/warning-signs/reference` | Get warning signs reference list |

---

## Frontend Screens

| Screen | File | Status |
|---|---|---|
| Home Dashboard | `HardwareDashboard.tsx` | Working |
| System Profile | `SystemProfileView.tsx` | Working with live hardware data |
| Diagnostic History | `DiagnosticHistoryView.tsx` | Backend-ready |
| Recurring Patterns | `RecurringPatternsView.tsx` | Backend-ready |
| Warning Signs | `WarningSignsView.tsx` | Working reference screen |
| Reports | `ReportsView.tsx` | Placeholder |
| New Diagnosis | In progress | Next module |

---

## Current Module Status

| Module | Status |
|---|---|
| System Profile Detection | Working |
| Home Dashboard | Working, needs database tables for full stats |
| Diagnostic History | Ready, waits for saved sessions |
| Recurring Patterns | Ready, waits for saved sessions |
| Warning Signs Reference | Working |
| New Diagnosis | Next to implement |
| Reports | Placeholder |

---

## Action Categories

| Category | Meaning |
|---|---|
| Monitor | Observe the issue because symptoms are mild or not yet repeated |
| Maintain | Perform basic cleanup, update, or maintenance steps |
| Troubleshoot | Investigate software, driver, OS, startup, or configuration causes |
| Escalate | Issue may require professional inspection or urgent attention |

---

## Confidence Labels

| Label | Meaning |
|---|---|
| High Confidence | Strong match between symptoms and diagnostic rule |
| Moderate | Some matching signals, but more checks may be needed |
| Low Confidence | Weak match or limited information |

---

## Hardware Detection Requirements

RigMD uses Windows-based hardware detection.

Required packages:

```txt
WMI
pywin32
psutil
```

Install manually if needed:

```bash
cd backend
venv\Scripts\activate
pip install WMI pywin32 psutil
```

### Supported OS

| OS | Support |
|---|---|
| Windows 10 | Supported |
| Windows 11 | Supported |
| Linux | Not supported for WMI hardware detection |
| macOS | Not supported for WMI hardware detection |

---

## What RigMD Detects

RigMD can detect:

- CPU model
- CPU usage
- RAM capacity and usage
- GPU model
- GPU driver
- Storage size
- Storage usage
- Storage type
- Windows version
- Chipset or motherboard proxy
- System age based on OS install date

---

## Troubleshooting

### 1. Frontend Shows “Connection Lost”

Cause:

```txt
Backend is not running or frontend cannot reach port 8000.
```

Fix:

```bash
cd D:\RigMD
backend\venv\Scripts\python.exe -m uvicorn backend.main:app --reload --port 8000
```

Then refresh:

```txt
http://localhost:5173
```

---

### 2. Orange Dashboard Database Warning Appears

Message:

```txt
Database dashboard data is not available yet. Check your Supabase connection and tables.
```

Cause:

- `DATABASE_URL` is missing
- `DATABASE_URL` still contains `[YOUR-PASSWORD]`
- Supabase password is wrong
- Supabase tables are missing
- Database route failed

Check:

```txt
http://localhost:8000/api/dashboard/summary
```

If the response contains:

```json
"database_warning": "..."
```

then the backend database query failed.

Fix your:

```txt
backend/.env
```

and confirm these tables exist in Supabase:

```txt
profiles
sessions
recommendations
```

---

### 3. Hardware Shows Unknown or Generic Values

Cause:

- WMI package missing
- pywin32 missing
- psutil missing
- WMI access blocked
- Backend is not running on Windows

Fix:

```bash
cd backend
venv\Scripts\activate
pip install WMI pywin32 psutil
```

Restart backend after installing.

---

### 4. Backend Cannot Import `backend`

Cause:

Backend was run from the wrong folder.

Wrong:

```bash
cd backend
uvicorn main:app --reload --port 8000
```

Correct:

```bash
cd D:\RigMD
backend\venv\Scripts\python.exe -m uvicorn backend.main:app --reload --port 8000
```

---

### 5. `psycopg2` Missing

Error:

```txt
ModuleNotFoundError: No module named 'psycopg2'
```

Fix:

```bash
cd backend
venv\Scripts\activate
pip install psycopg2-binary
```

Then restart backend.

---

## Environment Files

### backend/.env

```env
GEMINI_API_KEY=your_gemini_api_key_here
DATABASE_URL=your_supabase_connection_string_here
FRONTEND_URL=http://localhost:5173
```

### frontend/.env

```env
VITE_API_BASE_URL=http://localhost:8000
```

---

## Git Ignore Reminder

The following files must never be committed:

```txt
.env
.env.*
backend/.env
backend/.env.*
frontend/.env
frontend/.env.*
```

Keep only sample files if needed:

```txt
.env.example
backend/.env.example
frontend/.env.example
```

---

## Branch Strategy

| Branch | Purpose |
|---|---|
| `main` | Stable release branch |
| `dev` | Integration branch |
| `feature/[feature-name]` | Individual feature work |

Example:

```bash
git switch -c feature/rigmd-diagnostic-modules
```

---

## Commit Message Format

Use Conventional Commits:

```txt
feat(scope): add new feature
fix(scope): fix bug
chore(scope): update config or dependencies
docs(scope): update documentation
refactor(scope): improve code structure
```

Examples:

```txt
feat(history): add diagnostic history screen and API
feat(recurring): add recurring patterns screen and API
feat(warnings): add warning signs reference screen and API
fix(hardware): improve hardware refresh handling
docs(readme): update setup instructions
```

---

## Important Notes

- RigMD is scoped to Windows desktop PCs.
- RigMD does not physically inspect hardware.
- RigMD does not guarantee a final diagnosis.
- Advisory output is probabilistic and rule-based.
- Professional inspection is still required for severe or repeated hardware-related warning signs.
- Do not commit real API keys, Supabase passwords, or environment files.