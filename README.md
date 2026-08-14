# RigMD V2 — Autonomous PC Diagnostic & Remediation System

> **Draft / Migration README** — gitignored, not committed.  
> This document describes the next-generation architecture, what already exists, what will change, and the full structural hierarchy for the C# migration.

---

## 1. Overview & Vision

RigMD is evolving from a passive diagnostic decision-support system (DSS) into a **closed-loop autonomous diagnostic and remediation platform** for Windows desktop PCs.

The system will combine:
1. **Isolated Native Windows Telemetry** — hardware, drivers, event logs, processes.
2. **Deterministic Diagnostic Rule Engine** — structured scoring, reasoning factors, warning signs.
3. **Controlled Autonomous Remediation Engine** — preconditioned actions, dry-run simulation, execution, verification, automatic rollback, and intelligent pivoting.
4. **Offline-First Persistence** — local SQLite as the single source of truth, with optional cloud sync.
5. **Constrained AI Explanations** — plain-language summaries without delegating system execution to any LLM.

---

## 2. What RigMD Already Has (Confirmed Baseline)

### 2.1 Frontend (React / Vite)
The existing React frontend is substantial and will be preserved and redirected — not rewritten.

| Screen | Status |
|---|---|
| `SystemProfileView` | ✅ Fully implemented |
| `NewDiagnosisView` (8-step structured intake) | ✅ Fully implemented |
| `DiagnosticIntakeReview` | ✅ Fully implemented |
| `DiagnosticResultView` | ✅ Fully implemented |
| `DiagnosticHistoryView` | ✅ Fully implemented |
| `DiagnosticSessionDetailView` | ✅ Fully implemented |
| `RecurringPatternsView` | ✅ Fully implemented |
| `WarningSignsView` | ✅ Fully implemented |
| `HardwareDashboard` | ✅ Fully implemented |
| `HelpScopeView` | ✅ Fully implemented |
| `ReportsView` | ⚠️ Empty file — not yet implemented |

### 2.2 Backend (Python / FastAPI)
The existing backend is functional, not a skeleton.

**Confirmed working routers**: `hardware`, `dashboard`, `history`, `recurring`, `warning_signs`, `profile`, `remediation` (stub).

**`main.py` orchestrates the full pipeline**:
- `GET live hardware` → `run_diagnostic()` → `get_or_create_profile()` → `save_session()`

**Services that exist**:
- `diagnostic_engine.py` (34 KB) — substantial rule-based scoring, evidence, and confidence engine.
- `remediation_service.py` (20 KB) — early-stage; not fully integrated.
- `resolution_service.py` — re-evaluates a session against live hardware.
- `ai_explainer.py` — Gemini API client, constrained to diagnostic context.
- `session_store.py` — query helpers.

### 2.3 Hardware Detection
The legacy Python implementation already detects:
- CPU, GPU, GPU driver, RAM, OS version
- Storage type classification (NVMe / SSD / HDD), physical + logical disk metadata
- Browser and game/heavy process detection
- Chipset driver, system age
- Multiple WMI fallback/error paths

### 2.4 Data Models
**Profile** (currently `profiles` table):
- `cpu_model`, `ram_capacity`, `storage_type`, `storage_capacity`, `storage_details` (JSONB), `os_version`, `gpu_driver`, `chipset_driver`, `system_age`

**Session** (currently `sessions` table):
- 8 intake fields: `symptom_type`, `affected_activity`, `frequency`, `severity`, `duration`, `recent_changes`, `system_state`, `warning_signs`
- Diagnostic output: `diagnosed_category`, `action_category`, `confidence_label`, `ai_explanation`, `is_recurring`
- Resolution lifecycle: `resolution_status`, `resolution_checked_at`, `resolution_summary`, `resolution_proof` (JSON), `last_action_status`, `last_action_summary`

### 2.5 Closed-Loop Seed (Already Exists)
The resolution recheck mechanism is already a seed for the future autonomous loop:
- Mark session as `needs_recheck` after action.
- Re-run live hardware collection + resolution check.
- Update session state.

This is not yet formalized as `RemediationRun` / `VerificationResult`, but the concept exists in the codebase.

---

## 3. Old vs. New Comparison

| Dimension | Legacy / Baseline (V1) | Target (V2) |
|---|---|---|
| **Core Stack** | Python FastAPI / CustomTkinter | React + C#/.NET (ASP.NET Core + WPF + WebView2) |
| **System Behavior** | Passive Advisory | **Controlled Autonomous Remediation** (Closed-Loop) |
| **Remediation** | Manual action based on text | Automated with **Verify → Rollback → Pivot** |
| **Safety Model** | Static text warnings | **Multi-Tier Safety Policy + Dry-Run Mode** |
| **Hardware Detection** | Monolithic Python script (`wmi`, `psutil`) | **Isolated C# Providers** (`System.Management`, CIM, EventLogs) |
| **Primary Storage** | Supabase (cloud-dependent) | **SQLite (Local Source of Truth, 100% Offline)** |
| **Cloud Sync** | Required for basic save | **Optional Async Sync** (background Supabase worker) |
| **AI Role** | AI generates explanation | **AI constrained to explanation only** — never controls execution |
| **Packaging** | Python virtual environment | **Standalone Windows Executable** (WPF + WebView2) |

---

## 4. Architecture Flow

```text
                 ┌────────────────────────────────┐
                 │       React Frontend UI        │
                 │   (Intake / Live / History)    │
                 └───────────────┬────────────────┘
                                 │ HTTP / IPC
                                 ↓
                 ┌────────────────────────────────┐
                 │       ASP.NET Core API         │
                 │   (RigMD.Api / Minimal APIs)   │
                 └───────────────┬────────────────┘
                                 │
                                 ↓
                 ┌────────────────────────────────┐
                 │       Application Layer        │
                 │   (Use Cases & Orchestration)  │
                 └───────┬───────────────┬────────┘
                         │               │
         ┌───────────────┘               └───────────────┐
         ↓                                               ↓
┌─────────────────────┐                   ┌───────────────────────────────┐
│     Domain Layer    │                   │      Infrastructure Layer     │
│  - Diagnostic Rules │                   │  - Windows Native Providers   │
│  - Autonomous Core  │                   │  - EF Core + SQLite DbContext │
│  - Safety Policies  │                   │  - Gemini AI Explainer Client │
└─────────────────────┘                   │  - Optional Supabase Sync     │
                                          └──────────────┬────────────────┘
                                                         │
                              ┌──────────────────────────┴──────────────────────────┐
                              ↓                                                      ↓
                   ┌──────────────────────┐                           ┌──────────────────────────┐
                   │   Local SQLite DB    │                           │   Optional Supabase DB   │
                   │  (Source of Truth)   │                           │   (Async Cloud Sync)     │
                   └──────────────────────┘                           └──────────────────────────┘
```

---

## 5. New Solution & Repository Structure

```text
RigMD/
├── backend/                              ← Legacy Python/FastAPI (Reference baseline — do not delete yet)
│   ├── main.py                           ← Primary orchestration
│   ├── routers/                          ← hardware.py, dashboard.py, history.py, recurring.py, etc.
│   ├── services/                         ← diagnostic_engine.py, ai_explainer.py, remediation_service.py, etc.
│   ├── models/                           ← profile_model.py, session_model.py, recommendation_model.py
│   └── schemas/                          ← Pydantic request/response models
│
├── backend-dotnet/                       ← New C#/.NET Solution
│   ├── RigMD.sln
│   │
│   ├── RigMD.Domain/                     ← Enterprise business rules (zero external dependencies)
│   │   ├── Entities/                     ← SystemProfile, DiagnosticSession, DiagnosticOutput, etc.
│   │   ├── Enums/                        ← ActionCategory, RemediationTier, VerificationStatus, etc.
│   │   └── Rules/                        ← Diagnostic rule definitions, symptom matchers
│   │
│   ├── RigMD.Application/                ← Use cases, interfaces, orchestration
│   │   ├── Contracts/                    ← IProfileRepository, IDiagnosticSessionRepository, IRemediationRepository
│   │   ├── Providers/                    ← ICpuProvider, IGpuProvider, IStorageProvider, etc.
│   │   ├── Services/                     ← DiagnosticRuleService, ScoringService, EvidenceService, ConfidenceService, AdvisoryActionService
│   │   ├── Autonomous/                   ← IRemediationPlanner, ISafetyPolicy, IRemediationRegistry, IRemediationExecutor, IVerificationService, IRollbackManager, IPivotEngine
│   │   └── UseCases/                     ← RunDiagnosticUseCase, PlanRemediationUseCase, ExecuteDryRunUseCase
│   │
│   ├── RigMD.Infrastructure/             ← External adapters, OS integration, persistence
│   │   ├── Persistence/                  ← EF Core DbContext, SQLite migrations, repository implementations
│   │   ├── Windows/                      ← Native Windows information providers
│   │   │   ├── CpuInformationProvider.cs
│   │   │   ├── GpuInformationProvider.cs
│   │   │   ├── MemoryInformationProvider.cs
│   │   │   ├── StorageInformationProvider.cs
│   │   │   ├── DriverInformationProvider.cs
│   │   │   ├── EventLogProvider.cs
│   │   │   ├── ProcessInformationProvider.cs
│   │   │   └── WindowsSystemProfileProvider.cs
│   │   ├── Remediation/                  ← Concrete approved action handlers
│   │   ├── Ai/                           ← Gemini API client (constrained to explanation)
│   │   └── Sync/                         ← Optional Supabase background sync worker
│   │
│   ├── RigMD.Api/                        ← ASP.NET Core presentation & endpoint layer
│   │   ├── Endpoints/                    ← /api/health, /api/system-profile/live, /api/diagnostics, etc.
│   │   ├── Middleware/                   ← Exception handling, logging, validation pipeline
│   │   └── Program.cs                    ← DI setup, middleware, service registration
│   │
│   └── RigMD.Tests/                      ← Automated test suite
│       ├── Domain.Tests/                 ← Diagnostic rule & scoring unit tests
│       ├── Application.Tests/            ← Use case & dry-run pipeline tests
│       └── Infrastructure.Tests/         ← Windows provider & SQLite integration tests
│
├── frontend/                             ← React 18 + Vite (Preserved — redirected via VITE_API_URL)
│
├── Docs/                                 ← Thesis PDFs, SRS, SDD
│
├── AGENTS.md                             ← AI engineering policy (authoritative)
├── ARCHITECTURE.md                       ← Target architectural spec
├── ARCHITECTURE_MIGRATION.md             ← Old → new migration mapping
├── AUTONOMOUS_ENGINE.md                  ← Closed-loop engine concepts
├── REMEDIATION_POLICY.md                 ← Safety tiers and action consent model
├── DECISIONS.md                          ← Architectural decision records
├── C_SHARP_MIGRATION_PLAN.md             ← 17-phase roadmap and phase checklists
├── LEGACY_CAPABILITY_INVENTORY.md        ← File-by-file audit of what exists now
├── MIGRATION_MATRIX.md                   ← Per-capability migration tracking table
└── BASELINE.md                           ← Known state of the Python baseline at migration start
```

---

## 6. Domain Model & ERD Hierarchy

Reconciled from the thesis SDD ERD + new autonomous concepts:

```text
SystemProfile
  │ (1)
  └── (0..*) DiagnosticSession
               │ (1)
               ├── (1..*) SessionAnswer
               │
               └── (1) DiagnosticOutput
                         │
                         ├── (1..*) ReasoningFactor
                         │
                         ├── (0..*) OutputWarningSign ──> (1) WarningSign
                         │
                         └── (0..1) RemediationRun
                                      │
                                      ├── (1..*) ActionAttempt
                                      │            │
                                      │            └── (1) VerificationResult
                                      │
                                      ├── (0..*) RollbackEvent
                                      │
                                      └── (0..*) PivotEvent
```

> **Key rule**: Do not discard `SessionAnswer`, `ReasoningFactor`, or `OutputWarningSign` entities. The current Python implementation stores these as flat columns. The C# model promotes them to proper relational entities to align with the SDD ERD.

---

## 7. Closed-Loop Autonomous Pipeline

```text
  [ Intake Symptoms + Live Telemetry ]
                  ↓
       [ Deterministic Diagnosis ]
       (DiagnosticRuleService, ScoringService, EvidenceService)
                  ↓
     [ Probable Cause + Evidence + Confidence ]
                  ↓
         [ Remediation Planner ]
         (IRemediationPlanner + IRemediationRegistry)
                  ↓
      [ Safety Policy & Tier Check ]
      (ISafetyPolicy → Tier 1 / Tier 2 / Tier 3)
                  ↓
      ┌─────────────────────────────────┐
      │     DRY-RUN SIMULATION MODE     │  ← Always available; permanent developer tool
      └──────────────┬──────────────────┘
                     ↓ (if approved)
        [ Execute Action Attempt ]
        (IRemediationExecutor + registered action)
                     ↓
        [ Measure & Verify State ]
        (IVerificationService)
                     │
             ┌───────┴───────┐
             │   Resolved?   │
             └───────┬───────┘
           YES │           │ NO
               ↓           ↓
         [ Complete ]   [ IRollbackManager ]
         [ Audit Log ]       ↓
                      [ IPivotEngine ]
                             ↓
                     [ Next Candidate Action ]
                             ↓
                     [ Re-Verify / Escalate ]
```

---

## 8. Remediation Safety Tiers

| Tier | Type | Policy |
|---|---|---|
| **Tier 1** | Safe & Reversible | Automated execution with user notification. No consent required. |
| **Tier 2** | Configuration Changes | Requires explicit 1-click user consent. Restore snapshot captured before execution. |
| **Tier 3** | Hardware / High-Risk | Strictly Advisory. Never automated. |

---

## 9. What We Are NOT Building Yet

Until the foundation is solid, the following are explicitly out of scope:
- ❌ Large autonomous action catalog
- ❌ Arbitrary Windows command execution
- ❌ AI-generated Windows commands
- ❌ AI controlling the PC directly
- ❌ Unrestricted registry modification
- ❌ BIOS/firmware automation
- ❌ Aggressive "one-click optimize everything"
- ❌ Mandatory cloud synchronization

---

## 10. Development Status & Current Phase

For the full phased breakdown, see **[`C_SHARP_MIGRATION_PLAN.md`](./C_SHARP_MIGRATION_PLAN.md)**.  
For the file-by-file capability audit, see **[`LEGACY_CAPABILITY_INVENTORY.md`](./LEGACY_CAPABILITY_INVENTORY.md)**.  
For per-capability migration tracking, see **[`MIGRATION_MATRIX.md`](./MIGRATION_MATRIX.md)**.  
For the baseline state reference, see **[`BASELINE.md`](./BASELINE.md)**.

- **Current Phase**: Phase 7 (Connect the React Frontend) — **Milestone A (Vertical Slice Complete) Achieved**
- **Reference Tag**: `v0.1-python-baseline`
- **Active Branch**: `feature/csharp-migration`

**How to run current version:**
1. Backend: `cd backend-dotnet/RigMD.Api` then `dotnet run` (Listens on port 5273)
2. Frontend: `cd frontend` then `npm run dev` (Listens on port 5173, routes to 5273)

**Immediate next step**: Phase 8 — Rebuilding the intellectual core and Autonomous Remediation Engine in C#.
