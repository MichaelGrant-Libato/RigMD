# RigMD V2 — Windows PC Diagnostic & Remediation System

RigMD is a Windows desktop PC diagnostic advisory system that is transitioning into a controlled diagnostic and remediation platform.

The current application uses:

- React + Vite for the frontend
- C# / .NET with ASP.NET Core for the active backend
- Native Windows/WMI-based hardware telemetry
- Deterministic diagnostic rules
- Supabase PostgreSQL for the current persistence layer
- Controlled remediation actions
- Resolution rechecking
- Recurring pattern detection
- Warning-sign normalization
- Constrained AI explanations

The legacy Python/FastAPI backend is still kept in the repository temporarily as a migration reference and historical baseline.

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

## 2. Current Migration Status

The active backend migration from Python/FastAPI to C#/.NET has reached **functional parity** and has been officially merged into the `main` branch. 

### Legacy Python Reference

```
Tag: python-backend-final
```

### Current Development URLs

Frontend:

```
http://localhost:5173
```

C# backend:

```
http://localhost:5273
```

Frontend API configuration:

```
VITE_API_BASE_URL=http://localhost:5273
```

The frontend no longer depends on the Python backend at port 8000.

---

## 3. Current Technology Stack

| Layer | Current Implementation |
|---|---|
| Frontend | React + TypeScript + Vite |
| Backend API | C# / ASP.NET Core |
| Domain Logic | C# |
| Hardware Detection | WMI / System.Management |
| Current Database | SQLite (Local Source of Truth) |
| Database Client | Entity Framework Core (EF Core 10) |
| AI Explanation | Gemini integration |
| Testing | xUnit |
| Target Local Persistence | ✅ Implemented |
| Target Desktop Packaging | WPF + WebView2 |
| Target Cloud Strategy | Optional Supabase synchronization |

### Important Persistence Note

The C# backend has successfully migrated to using local **SQLite via Entity Framework Core** as the primary offline-first source of truth.

The database is automatically created in the local AppData directory:
`%LOCALAPPDATA%/RigMD/rigmd.db`

The previous direct Supabase integration (`DatabaseSessionService`) has been completely removed in favor of the Repository pattern.

The long-term cloud architecture includes:
- SQLite = local source of truth
- Supabase = optional asynchronous cloud synchronization (future)

---

## 4. Frontend

The existing React frontend was preserved rather than rewritten.

| Screen | Status |
|---|---|
| HardwareDashboard | ✅ Implemented |
| SystemProfileView | ✅ Implemented |
| NewDiagnosisView | ✅ Implemented |
| DiagnosticIntakeReview | ✅ Implemented |
| DiagnosticResultView | ✅ Implemented |
| DiagnosticHistoryView | ✅ Implemented |
| DiagnosticSessionDetailView | ✅ Implemented |
| RecurringPatternsView | ✅ Implemented |
| WarningSignsView | ✅ Implemented |
| HelpScopeView | ✅ Implemented |

The frontend now uses `VITE_API_BASE_URL` for active API calls.

---

## 5. Current C# Backend Capabilities

### 5.1 Hardware Detection

The C# backend provides live Windows hardware and system information including:

- CPU
- GPU
- GPU driver
- RAM
- operating system
- storage
- storage type
- motherboard
- chipset/system metadata
- running processes
- system utilization

Primary endpoints:

```
GET  /api/hardware/live
POST /api/hardware/refresh
```

### 5.2 Diagnostic Engine

The diagnostic engine has been migrated to C#.
It supports:

- structured symptom intake
- deterministic category classification
- evidence generation
- action-category selection
- confidence classification
- session persistence

Primary endpoint:

```
POST /api/diagnosis/submit
```

Diagnostic categories include areas such as:

- OS performance degradation
- driver conflict
- thermal condition
- storage health behavior
- display driver behavior
- boot and startup failure

### 5.3 Diagnostic History

Endpoints:

```
GET /api/diagnosis/sessions
GET /api/diagnosis/sessions/{sessionId}
```

History supports dynamically calculated recurring status rather than relying only on previously stored recurrence values.

### 5.4 Dashboard

Endpoint:

```
GET /api/dashboard/summary
```

Dashboard data includes:

- total diagnostic sessions
- sessions this month
- escalated sessions
- recurring issue count
- active warning-sign count
- action distribution
- session frequency
- latest session
- recent normalized warning signs

### 5.5 Hardware Profiles

The active frontend currently uses:

```
POST /api/profiles/save
```

Unused legacy profile list/read/update endpoints were intentionally not recreated because no active frontend feature depends on them.

### 5.6 Recurring Patterns

Endpoints:

```
GET /api/recurring/patterns
GET /api/recurring/patterns/{patternId}
```

Recurring patterns are calculated from diagnostic history.
Current recurring detection operates dynamically rather than depending only on stored `is_recurring` values.

### 5.7 Warning Signs

Endpoint:

```
GET /api/warning-signs/reference
```

The warning-sign reference supports:

- normalized warning-sign definitions
- observed occurrence counts
- category filtering
- text search
- observed-only filtering

### 5.8 Remediation

Current remediation endpoints:

```
GET  /api/remediation/actions
POST /api/remediation/execute
POST /api/remediation/open-target
```

The remediation implementation is controlled and limited to explicitly supported actions.
It is not an unrestricted system-command execution engine.

### 5.9 Resolution Checking

Endpoints:

```
POST /api/diagnosis/{sessionId}/needs-recheck
POST /api/diagnosis/{sessionId}/check-resolution
```

This provides the current foundation for a closed-loop workflow:

```
Diagnose
   ↓
Recommend / Execute Supported Action
   ↓
Mark Needs Recheck
   ↓
Collect Current System State
   ↓
Check Resolution
   ↓
Resolved / Still Active / Needs Recheck
```

### 5.10 Database Health

Endpoint:

```
GET /api/database/health
```

This verifies current database connectivity.

---

## 6. Old vs. Current vs. Target Architecture

| Dimension | Legacy V1 | Current Runtime | Target V2 |
|---|---|---|---|
| Backend | Python / FastAPI | C# / ASP.NET Core | C# / ASP.NET Core |
| Frontend | React / Vite | React / Vite | React + WPF/WebView2 |
| Hardware Detection | Python WMI / wrappers | C# WMI providers | Native C# providers |
| Diagnostic Engine | Python | C# | C# Domain layer |
| Persistence | Supabase PostgreSQL | Supabase PostgreSQL | SQLite local source of truth |
| Cloud | Required | Required for current persistence | Optional sync |
| Remediation | Mostly advisory | Controlled supported actions | Closed-loop remediation |
| Verification | Basic resolution recheck | Resolution recheck | Formal verification |
| Rollback | Not formalized | Not formalized | Planned |
| Pivot | Not formalized | Not formalized | Planned |
| AI | Explanation | Explanation only | Explanation |
| Packaging | Development runtime | Development runtime | Standalone Windows application |

---

## 7. Current Runtime Architecture

```
                 ┌──────────────────────────────┐
                 │       React Frontend         │
                 │       Vite / TypeScript      │
                 └──────────────┬───────────────┘
                                │
                                │ HTTP
                                ↓
                 ┌──────────────────────────────┐
                 │       ASP.NET Core API       │
                 │          RigMD.Api           │
                 └──────────────┬───────────────┘
                                │
                                ↓
                 ┌──────────────────────────────┐
                 │      Application Layer       │
                 │ Services / Models / Logic    │
                 └──────────────┬───────────────┘
                                │
                    ┌───────────┴───────────┐
                    ↓                       ↓
         ┌─────────────────────┐   ┌─────────────────────┐
         │     Domain Layer    │   │ Infrastructure Layer│
         │ Diagnostic Rules    │   │ Windows Providers   │
         │ Categories          │   │ Database Access      │
         │ Confidence          │   │ Remediation          │
         └─────────────────────┘   └──────────┬──────────┘
                                             │
                                             ↓
                                  ┌───────────────────────┐
                                  │ Supabase PostgreSQL   │
                                  │       via Npgsql      │
                                  └───────────────────────┘
```

---

## 8. Target V2 Architecture

```
                 ┌──────────────────────────────┐
                 │        React UI              │
                 │     WPF + WebView2           │
                 └──────────────┬───────────────┘
                                ↓
                 ┌──────────────────────────────┐
                 │       ASP.NET Core           │
                 └──────────────┬───────────────┘
                                ↓
                 ┌──────────────────────────────┐
                 │      Application Layer       │
                 └──────────────┬───────────────┘
                                ↓
                 ┌──────────────────────────────┐
                 │         Domain Layer         │
                 │ Diagnosis / Safety / Rules   │
                 └──────────────┬───────────────┘
                                ↓
                 ┌──────────────────────────────┐
                 │     Infrastructure Layer     │
                 │                              │
                 │  Windows Native Providers    │
                 │  SQLite Persistence          │
                 │  AI Explanation Client       │
                 │  Optional Cloud Sync         │
                 └──────────────┬───────────────┘
                                │
                    ┌───────────┴───────────┐
                    ↓                       ↓
          ┌──────────────────────┐   ┌─────────────────────┐
          │    Local SQLite DB   │   │ Supabase PostgreSQL │
          │   Source of Truth    │   │ Optional Cloud Sync │
          └──────────────────────┘   └─────────────────────┘
```

---

## 9. Repository Structure

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
│   └── REMEDIATION_POLICY.md
│
├── AGENTS.md
├── IMPLEMENT_ME.md
├── README.md
└── .gitignore
```

---

## 10. Domain Model Direction

The target C# domain model includes:

```
SystemProfile
  │
  └── DiagnosticSession
        │
        ├── SessionAnswer
        │
        └── DiagnosticOutput
              │
              ├── ReasoningFactor
              ├── OutputWarningSign ──> WarningSign
              │
              └── RemediationRun
                    │
                    ├── ActionAttempt
                    │     └── VerificationResult
                    │
                    ├── RollbackEvent
                    └── PivotEvent
```

The full target model is still being formalized.

The migration should not discard concepts from the SDD simply because the legacy database stores several values in flat columns.

---

## 11. Controlled Remediation Direction

The future closed-loop pipeline is:

```
Symptoms + Live Telemetry
           ↓
Deterministic Diagnosis
           ↓
Evidence + Confidence
           ↓
Remediation Candidate
           ↓
Safety Policy
           ↓
Dry Run
           ↓
User Approval When Required
           ↓
Execute Registered Action
           ↓
Verify
           ↓
Resolved?
      ┌────┴────┐
      │         │
     Yes        No
      │         │
      ↓         ↓
  Complete   Rollback
                ↓
               Pivot
                ↓
        Next Safe Action
```

Only explicitly registered and reviewed remediation actions may execute.
AI must never generate arbitrary Windows commands for automatic execution.

---

## 12. Remediation Safety Tiers

| Tier | Type | Policy |
|---|---|---|
| Tier 1 | Low-risk and reversible | May execute with clear user visibility |
| Tier 2 | Configuration-changing | Requires explicit user approval and recovery strategy |
| Tier 3 | High-risk / hardware / firmware | Advisory only |

Restricted functionality includes:

- arbitrary Windows command execution
- unrestricted PowerShell execution
- AI-generated system commands
- uncontrolled registry modification
- BIOS or firmware automation
- destructive storage operations
- uncontrolled one-click optimization

---

## 13. Running RigMD

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

## 14. Build and Test

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

## 15. Legacy Python Backend

The directory:

```
backend/
```

contains the previous FastAPI implementation.
It is no longer the active backend used by the React frontend.

It remains temporarily for:

- regression comparison
- migration verification
- historical reference
- auditability

The Python runtime should only be removed after:

- automated C# test coverage is expanded
- full UI smoke testing passes
- no active runtime dependency remains
- documentation cleanup is complete

Historical documentation should remain after runtime retirement.

Relevant files include:

- `Docs/BASELINE.md`
- `Docs/LEGACY_CAPABILITY_INVENTORY.md`

---

## 16. Current Development Status

### Completed

- ✅ C# solution foundation
- ✅ ASP.NET Core API
- ✅ Windows hardware observation layer
- ✅ diagnostic engine migration
- ✅ frontend API migration from port 8000 to 5273
- ✅ diagnostic persistence compatibility
- ✅ diagnostic history
- ✅ dashboard
- ✅ recurring pattern detection
- ✅ warning-sign reference
- ✅ profile save
- ✅ remediation endpoints
- ✅ resolution rechecking
- ✅ frontend production build
- ✅ .NET build
- ✅ .NET test execution
- ✅ route parity review
- ✅ migration checkpoint
- ✅ generated bin/ and obj/ files removed from Git tracking

### In Progress / Remaining

- ⏳ expand automated test coverage
- ⏳ improve Application/Infrastructure separation
- ⏳ replace anonymous/reflection-based internal models
- ⏳ improve database aggregation queries
- ⏳ formalize remediation planner
- ⏳ formalize safety policy
- ⏳ add dry-run remediation
- ⏳ formalize verification
- ⏳ add rollback
- ⏳ add pivot logic
- ✅ implement SQLite local persistence
- ⏳ implement optional Supabase synchronization
- ⏳ desktop packaging
- ⏳ final thesis/SRS/SDD alignment
- ⏳ retire legacy Python runtime

---

## 17. Important Git Checkpoints

**Legacy Python Reference**

```
python-backend-final
```

**C# Runtime Migration Complete**

```
csharp-migration-complete
```

Current migration checkpoint:

```
983190e feat: complete C# backend migration parity
```

---

## 18. Documentation

- [Architecture](./Docs/ARCHITECTURE.md)
- [Architecture Migration](./Docs/ARCHITECTURE_MIGRATION.md)
- [C# Migration Roadmap](./Docs/C_SHARP_MIGRATION_PLAN.md)
- [Autonomous Engine](./Docs/AUTONOMOUS_ENGINE.md)
- [Remediation Policy](./Docs/REMEDIATION_POLICY.md)
- [Migration Matrix](./Docs/MIGRATION_MATRIX.md)
- [Legacy Baseline](./Docs/BASELINE.md)
- [Legacy Capability Inventory](./Docs/LEGACY_CAPABILITY_INVENTORY.md)
- [Architectural Decisions](./Docs/DECISIONS.md)

---

## 19. Current Development Focus

The Python-to-C# runtime migration is functionally complete for the active React frontend.

The next priorities are:

- expand meaningful automated tests
- clean Application and Infrastructure boundaries
- continue the controlled remediation framework
- implement local SQLite persistence
- implement optional Supabase synchronization
- perform full UI and backend validation
- retire the Python runtime
- prepare Windows desktop packaging
- align final implementation with the SRS and SDD