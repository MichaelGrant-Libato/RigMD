# RigMD — Windows PC Diagnostic & Remediation System

RigMD is a Windows desktop PC diagnostic advisory system that is transitioning into a controlled diagnostic and remediation platform.

---

## 1. Overview & Vision

RigMD is evolving from a passive diagnostic decision-support system into a **controlled closed-loop diagnostic and remediation platform** for Windows desktop PCs.

The target architecture combines:

1. **Native Windows Telemetry**
   Hardware, drivers, processes, utilization, and Windows system information.

2. **Deterministic Diagnostic Engine**
   Structured scoring, evidence, action categories, confidence levels, recurring-pattern analysis, and warning-sign detection.

3. **Controlled Remediation Engine**
   Approved remediation actions, safety checks, resolution rechecking, and future verification, rollback, and pivot behavior.

4. **Offline-First Persistence**
   The target architecture uses local SQLite as the source of truth, with optional Supabase synchronization.

5. **Constrained AI Explanations**
   AI may explain or summarize diagnostic results, but does not control system execution.

---

## 2. Current Technology Stack

| Layer | Implementation |
|---|---|
| Frontend | React + TypeScript + Vite |
| Backend API | C# / ASP.NET Core |
| Hardware Telemetry | Native Windows WMI / System.Management |
| Diagnostic Engine | Deterministic C# Domain layer |
| Persistence | SQLite (Local Source of Truth) & Supabase (Optional/Legacy Sync) |
| Remediation | Controlled Remediation Engine |
| AI Integration | Constrained Gemini Explanations |

---

## 3. Repository Structure

```
RigMD/
│
├── backend/
│   └── Legacy Python/FastAPI implementation
│       retained temporarily for migration reference
│
├── backend-dotnet/
│   │
│   ├── RigMD.sln
│   │
│   ├── RigMD.Api/
│   │   ├── Controllers/
│   │   └── Program.cs
│   │
│   ├── RigMD.Application/
│   │   ├── Models/
│   │   └── Services/
│   │
│   ├── RigMD.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   └── Rules/
│   │
│   ├── RigMD.Infrastructure/
│   │   ├── Windows/
│   │   └── DatabaseSessionService.cs
│   │
│   └── RigMD.Tests/
│
├── frontend/
│   └── React + TypeScript + Vite
│
├── Docs/
│   ├── ARCHITECTURE.md
│   ├── ARCHITECTURE_MIGRATION.md
│   ├── AUTONOMOUS_ENGINE.md
│   ├── BASELINE.md
│   ├── C_SHARP_MIGRATION_PLAN.md
│   ├── DECISIONS.md
│   ├── LEGACY_CAPABILITY_INVENTORY.md
│   ├── MIGRATION_MATRIX.md
│   ├── MIGRATION_HISTORY.md
│   └── REMEDIATION_POLICY.md
│
├── AGENTS.md
├── IMPLEMENT_ME.md
├── README.md
└── .gitignore
```

---

## 4. Controlled Remediation & Safety

RigMD employs a controlled, closed-loop remediation engine that executes explicitly approved actions, verifies resolutions, and safely handles rollbacks or pivots upon failure. Remediation actions are strictly classified into three safety tiers (Low-risk, Configuration-changing, and High-risk), ensuring that destructive or unverified operations require explicit user consent or remain advisory-only; see [`Docs/REMEDIATION_POLICY.md`](./Docs/REMEDIATION_POLICY.md) for full details.

---

## 5. Running RigMD

### Requirements

Install:

- compatible .NET SDK
- Node.js
- npm
- Windows
- valid local backend secrets/configuration

Do not commit:

- database passwords
- API keys
- Supabase credentials
- Gemini API keys
- real `.env` files

### Start the Backend

From the repository root:

```bash
cd backend-dotnet
dotnet run --project RigMD.Api
```

Expected backend URL:

```
http://localhost:5273
```

### Start the Frontend

Open another terminal:

```bash
cd frontend
npm install
npm run dev
```

Expected frontend URL:

```
http://localhost:5173
```

Ensure the frontend environment contains:

```
VITE_API_BASE_URL=http://localhost:5273
```

---

## 6. Build and Test

### Backend

```bash
cd backend-dotnet
dotnet build
dotnet test
```

### Frontend

```bash
cd frontend
npm run build
```

At the C# migration checkpoint:

- Frontend production build: **PASS**
- .NET build: **PASS**
- .NET test execution: **PASS**

Current automated test coverage is still limited and must be expanded.

Priority tests include:

- diagnostic-rule classification
- confidence calculation
- recurring-pattern detection
- warning-sign normalization
- resolution-state evaluation
- remediation safety behavior
- database integration behavior

---

## 7. Documentation

- [Architecture](./Docs/ARCHITECTURE.md)
- [Architecture Migration](./Docs/ARCHITECTURE_MIGRATION.md)
- [C# Migration Roadmap](./Docs/C_SHARP_MIGRATION_PLAN.md)
- [Autonomous Engine](./Docs/AUTONOMOUS_ENGINE.md)
- [Remediation Policy](./Docs/REMEDIATION_POLICY.md)
- [Migration Matrix](./Docs/MIGRATION_MATRIX.md)
- [Legacy Baseline](./Docs/BASELINE.md)
- [Legacy Capability Inventory](./Docs/LEGACY_CAPABILITY_INVENTORY.md)
- [Architectural Decisions](./Docs/DECISIONS.md)
- [Migration History](./Docs/MIGRATION_HISTORY.md)

---

## 8. Troubleshooting & Common Pitfalls

If you are setting up the C# environment for the first time or testing the Agent locally, watch out for these common issues:

### 1. `DATABASE_URL is not configured` (API Crash)
**The Problem:** The `AgentRepository` currently still relies on Supabase (PostgreSQL) instead of the local SQLite database. If your `.env` or `appsettings.Development.json` is missing the `DATABASE_URL`, the API will crash on startup or when the agent heartbeats.
**The Fix:** Add `DATABASE_URL` to `backend-dotnet/RigMD.Api/appsettings.Development.json`. 

### 2. API Hangs / Error 500 `TimeoutException` (Npgsql & PgBouncer)
**The Problem:** If your `DATABASE_URL` uses port `6543`, you are connecting to Supabase's `PgBouncer` connection pooler. The C# `.NET Npgsql` driver uses prepared statements by default, which are incompatible with PgBouncer in Transaction Mode, causing queries to hang and time out.
**The Fix:** Change the port in your `DATABASE_URL` from `6543` to `5432` to connect directly to the Postgres instance.

### 3. Frontend Shows Another PC (e.g., "MIKMIKYULAPPY")
**The Problem:** You copied `VITE_AGENT_ID` from a co-developer's `.env` file instead of using your own. The API correctly queried Supabase for that ID, returning your co-worker's PC hardware.
**The Fix:** 
1. Open `C:\ProgramData\RigMD\agent.json` on your local machine.
2. Copy the `AgentId`.
3. Paste it into `frontend/.env.local` as `VITE_AGENT_ID=your-local-guid`.
4. Restart the Vite dev server.

### 4. Agent "Offline" / Not Running as a Service
**The Problem:** The RigMD Agent may not be installed natively as a Windows Service on your development machine yet. 
**PowerShell Gotcha:** If you try to check the service status in PowerShell using `sc qc RigMDAgent`, it will **not** query the service. `sc` in PowerShell is an alias for `Set-Content`! You will accidentally create a text file named `qc` with the text "RigMDAgent".
**The Fix:** Use `sc.exe query RigMDAgent` in PowerShell, or just manually run the agent for testing: `dotnet run --project RigMD.Agent`.