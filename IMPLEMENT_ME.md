# IMPLEMENT_ME.md — RigMD 17-Phase Autonomous Engine Roadmap

This document is the **canonical implementation reference** for all developers and AI agents working on RigMD. It describes every phase of the migration from the Python advisory system to the C#/.NET closed-loop autonomous diagnostic and remediation platform.

---

## How to Read This Document

Each phase has:
- **Purpose** — why it exists and what problem it solves
- **Function** — what the code actually does
- **Key Files/Components** — what was/should be created
- **Acceptance Criteria** — how to know the phase is truly complete
- **Status** — current state

---

## Phase 0 — Baseline & Architecture Alignment ✅ COMPLETE

**Purpose:**  
Before writing a single line of C#, this phase ensures the entire team has documented and agreed on where the system is going. Without this, any code written would be built on an undefined target.

**Function:**  
Establish architecture documentation that describes the new system's layers, data flow, safety model, and the exact differences from the old Python system. This is the foundation that all subsequent phases reference.

**Key Files:**
- `Docs/ARCHITECTURE.md` — describes the 4-layer C# architecture (Domain → Application → Infrastructure → API)
- `Docs/AUTONOMOUS_ENGINE.md` — describes the closed-loop autonomous pipeline concept
- `Docs/REMEDIATION_POLICY.md` — defines the 3-tier safety model (Tier 1 = auto, Tier 2 = consent, Tier 3 = advisory only)
- `DECISIONS.md` — records all major architectural decisions and the reasoning behind them

**Acceptance Criteria:**
- [x] Final target architecture documented
- [x] Autonomous engine architecture documented
- [x] Remediation safety policy documented
- [x] SQLite vs Supabase responsibilities agreed upon and recorded
- [x] Scope changes explicitly recorded

---

## Phase 1 — Tag Legacy Code & Branching ✅ COMPLETE

**Purpose:**  
Preserve the exact Python baseline state so that it can always be referenced, compared against, and rolled back to if needed. The Python backend is a working system and must not be lost.

**Function:**  
Create a Git tag at the last known good state of the Python baseline. Create the active migration branch. Write inventory documentation that lists every capability of the Python system so nothing is accidentally dropped during migration.

**Key Files:**
- `Docs/BASELINE.md` — describes the exact state of the Python system at migration start
- `Docs/LEGACY_CAPABILITY_INVENTORY.md` — file-by-file audit of every Python module and what it does
- `Docs/MIGRATION_MATRIX.md` — per-capability tracking table: Python source → C# target → status
- Git tag: `v0.1-python-baseline`
- Git branch: `feature/csharp-migration`

**Acceptance Criteria:**
- [x] Python baseline tagged in Git
- [x] Migration branch created
- [x] All baseline documentation written

---

## Phase 2 — Build the C#/.NET Foundation ✅ COMPLETE

**Purpose:**  
Create the physical solution structure that all future phases will build inside. The project structure enforces architectural boundaries — it is impossible to accidentally let Infrastructure code depend on API code if the projects are structured correctly.

**Function:**  
Initialize the .NET solution with 5 targeted projects, each representing one architectural layer. Configure project references so that dependencies only flow inward (API → Application → Domain).

**Key Files:**
- `backend-dotnet/RigMD.sln`
- `backend-dotnet/RigMD.Domain/` — zero external dependencies, pure business concepts
- `backend-dotnet/RigMD.Application/` — use cases, interfaces, orchestration
- `backend-dotnet/RigMD.Infrastructure/` — OS integration, persistence, external APIs
- `backend-dotnet/RigMD.Api/` — ASP.NET Core presentation layer
- `backend-dotnet/RigMD.Tests/` — test project

**Acceptance Criteria:**
- [x] `dotnet build` succeeds for the entire solution
- [x] Projects only reference inward (Infrastructure can reference Application; Application cannot reference Infrastructure)

---

## Phase 3 — Final Domain Model & ERD ✅ COMPLETE

**Purpose:**  
Define the precise data model that the entire system will use. The Python implementation stored most data as flat JSON blobs. The C# model promotes those into proper relational entities aligned with the thesis SDD ERD — this is critical for academic validity.

**Function:**  
Create C# entity classes for every concept in the system. These classes are used by EF Core (persistence), the diagnostic engine (rules), and the autonomous pipeline (remediation tracking).

**Key Files (all in `RigMD.Domain/Entities/`):**
- `SystemProfile.cs` — hardware snapshot (CPU, GPU, RAM, Storage, OS)
- `DiagnosticSession.cs` — one diagnosis run, linked to a SystemProfile
- `SessionAnswer.cs` — the 8 intake answers (symptom, frequency, severity, etc.)
- `DiagnosticOutput.cs` — the result: diagnosed category, confidence, AI explanation
- `ReasoningFactor.cs` — individual evidence points that contributed to the diagnosis
- `WarningSign.cs` / `OutputWarningSign.cs` — flagged anomalies per session
- `RemediationRun.cs` — tracks one full autonomous remediation attempt
- `ActionAttempt.cs` — one specific action tried within a RemediationRun
- `VerificationResult.cs` — the measurement taken after an action to check if it worked
- `RollbackEvent.cs` — records a rollback attempt and its outcome
- `PivotEvent.cs` — records when the engine gave up on one action and tried another

**Acceptance Criteria:**
- [x] All entities defined with correct relationships
- [x] EF Core can generate migrations from these entities

---

## Phase 4 — Windows Observation Layer ✅ COMPLETE

**Purpose:**  
Replace the monolithic Python WMI scanner with isolated, testable, independently-mockable C# providers. Each provider has a single responsibility and can be replaced or mocked in tests without touching others.

**Function:**  
Use `System.Management` (WMI), CIM, and `System.Diagnostics` to query the Windows operating system for live hardware and process data. Each provider implements a clean interface defined in the Application layer.

**Key Interfaces (in `RigMD.Application/Contracts/Providers/`):**
- `ICpuProvider` / `IGpuProvider` / `IMemoryProvider` / `IStorageProvider`
- `IMotherboardProvider` / `IOperatingSystemProvider` / `IProcessProvider`

**Key Implementations (in `RigMD.Infrastructure/Windows/`):**
- `WmiCpuProvider.cs` — queries CPU usage, model, cores via WMI
- `WmiGpuProvider.cs` — GPU name, driver version
- `WmiMemoryProvider.cs` — RAM total and usage
- `WmiStorageProvider.cs` — disk type (NVMe/SSD/HDD), size, usage per drive
- `WmiMotherboardProvider.cs` — manufacturer, model
- `WmiOperatingSystemProvider.cs` — Windows version, build, uptime
- `ProcessProvider.cs` — detects running browsers, games, and heavy processes
- `WindowsSystemProfileService.cs` — aggregates all providers into one `HardwareProfileDto`

**Acceptance Criteria:**
- [x] Each provider compiles and returns non-null data on a real Windows machine
- [x] `GET /api/hardware/live` returns correct live system data

---

## Phase 5 — SQLite + EF Core Persistence 🔲 PENDING

### Purpose
Move RigMD toward full offline operation by using SQLite as the local source of truth.

### Current Verified State

The active C# backend does not yet use Entity Framework Core or SQLite.

Current persistence is:

ASP.NET Core
    ↓
DatabaseSessionService
    ↓
Npgsql
    ↓
Supabase PostgreSQL

Repository verification confirmed:
- no RigMdDbContext
- no EF Core persistence implementation
- no UseSqlite
- no SQLite repository implementations
- controllers currently depend directly on DatabaseSessionService

The current Npgsql/Supabase implementation is retained as a migration-compatibility persistence layer.

The target remains:

Application
    ↓
Repository Contracts
    ↓
Entity Framework Core
    ↓
SQLite
    ↓
Optional Supabase Sync


### Acceptance Criteria
- [ ] Application repository contracts are implemented
- [ ] EF Core SQLite provider is configured
- [ ] RigMdDbContext is implemented
- [ ] EF Core migration successfully creates the SQLite database
- [ ] Diagnostic sessions persist locally
- [ ] Sessions survive backend restart without Supabase
- [ ] Core diagnostic functionality works without internet

---

## Phase 6 — ASP.NET Core API ✅ COMPLETE

**Purpose:**  
Expose the C# business logic to the React frontend via a REST API. The API layer is thin — it only receives HTTP requests, calls Application services, and returns responses. No business logic lives in controllers.

**Function:**  
Create ASP.NET Core controllers for every route the frontend expects. Configure CORS so the React dev server (port 5173) can communicate with the API (port 5273).

**Key Endpoints:**
- `GET /api/hardware/live` — live system stats
- `POST /api/hardware/refresh` — force hardware cache reset
- `GET /api/dashboard/summary` — session counts and quick stats
- `POST /api/diagnosis/submit` — run the diagnostic engine
- `GET /api/diagnosis/sessions` — list all past sessions
- `GET /api/diagnosis/sessions/{id}` — session detail
- `POST /api/diagnosis/{id}/check-resolution` — re-evaluate a session
- `GET /api/remediation/actions` — list available remediation actions
- `POST /api/remediation/execute` — execute a remediation action
- `POST /api/remediation/open-target` — open a verification window

**Acceptance Criteria:**
- [x] All endpoints return HTTP 200 for valid inputs
- [x] CORS is configured so the React app can call the API without errors

---

## Phase 7 — Connect the React Frontend ✅ COMPLETE

**Purpose:**  
Stop the React frontend from talking to the old Python backend and redirect all API calls to the new C# backend. This is the moment the two systems become integrated.

**Function:**  
Configure the Vite environment variable `VITE_API_URL` to point to the C# backend URL. Ensure every API call in the frontend uses this variable rather than a hardcoded URL.

**Key Files:**
- `frontend/.env.local` — sets `VITE_API_URL=http://localhost:5273`
- All API call sites in `frontend/src/` must read from this env variable

**Acceptance Criteria:**
- [x] React app starts without "Connection lost" errors
- [x] Hardware Dashboard shows live C# data
- [x] New Diagnosis flow works end-to-end

---

## Phase 8 — Migrate the Diagnostic Engine ✅ COMPLETE

**Purpose:**  
The diagnostic engine is the intellectual core of RigMD — the rule-based scoring algorithm that maps symptoms + hardware state to a probable cause and confidence level. This must be ported with exact parity to the Python version so that academic results are reproducible.

**Function:**  
Translate `diagnostic_engine.py` (34 KB, the largest and most complex Python file) into C#. The engine takes a structured symptom payload and live hardware metrics, runs all diagnostic rules, scores each candidate category, and returns a `DiagnosticOutput` with confidence, reasoning factors, and a recommended action.

**Key Files:**
- `RigMD.Domain/Rules/DiagnosticEngine.cs` — pure static rule engine (no dependencies)
- `RigMD.Application/Services/DiagnosticEngineService.cs` — orchestrates: get hardware → run engine → get AI explanation → return DTO
- `RigMD.Application/Services/IDiagnosticEngineService.cs` — interface for the above
- `RigMD.Infrastructure/Ai/OfflineAiExplainer.cs` — offline fallback AI explainer

**Acceptance Criteria:**
- [x] Given the same symptom inputs, C# engine produces the same category and confidence as Python
- [x] `POST /api/diagnosis/submit` returns a complete diagnostic report
- [x] Works fully offline (no internet required)

---

## Phase 9 — Build the Autonomous Remediation Framework ✅ COMPLETE

### Purpose
Define the contracts and core structure for controlled autonomous remediation before expanding real Windows actions.

### Current Verified State

The following autonomy contracts exist:

- `IRemediationRegistry`
- `IRemediationPlanner`
- `ISafetyPolicy`
- `IRemediationExecutor`
- `IVerificationService`
- `IRollbackManager`
- `IPivotEngine`
- `IAutonomousOrchestrator`

Concrete implementations also exist for:

- remediation registry
- remediation planner
- safety policy
- autonomous orchestrator
- dry-run executor
- Windows remediation executor
- verification service

### Current Registered Actions

The current remediation registry contains:

- `clear_user_temp_files`
- `restart_explorer`
- `flush_dns`

Only `clear_user_temp_files` currently has a verified real Windows execution implementation.

The other registered actions must not be considered fully implemented merely because they exist in the registry.

### Acceptance Criteria

- [x] Autonomy interfaces compile successfully
- [x] Registry exists
- [x] Planner exists
- [x] Safety policy exists
- [x] Orchestrator exists
- [x] Dry-run executor exists
- [x] Real Windows executor exists
- [x] Verification service exists
- [ ] Additional registered actions are implemented and tested

---

## Phase 10 — Dry-Run Autonomous Engine ✅ COMPLETE

### Purpose
Provide a safe simulation mode that runs the remediation planning and safety pipeline without modifying Windows.

### Current Verified State

The dry-run architecture exists through:

- `IDryRunRemediationExecutor`
- `DryRunRemediationExecutor`
- `AutonomousOrchestrator`
- `POST /api/autonomy/dry-run`

A safety issue was discovered during verification.

The dry-run and real execution paths previously shared the same `IRemediationExecutor` registration, which allowed the dry-run path to resolve the real `WindowsRemediationExecutor`.

The implementation has been refactored so that:

/api/autonomy/dry-run
        ↓
RunDryRunCycleAsync
        ↓
IDryRunRemediationExecutor
        ↓
DryRunRemediationExecutor

while real execution uses:

/api/autonomy/execute
        ↓
RunExecutionCycleAsync
        ↓
IRemediationExecutor
        ↓
WindowsRemediationExecutor

Regression tests were added to keep the two execution paths isolated.

### Current Validation
- `dotnet build` — PASS
- `dotnet test` — 8 passed, 0 failed
- dry-run executor isolation test — PASS
- real executor isolation test — PASS
- safety rejection test — PASS

### Acceptance Criteria
- [x] Dry-run executor exists
- [x] Dry-run endpoint exists
- [x] Planner and safety policy run during dry run
- [x] Dry-run executor performs no real Windows changes
- [x] Real executor is isolated from dry-run execution
- [x] Regression tests verify executor separation
- [x] Safety fix reviewed and merged into `main`

---

## Phase 11 — First Real Autonomous Remediation 🟡 PARTIALLY COMPLETE

### Purpose
Introduce the first narrowly scoped real Windows remediation action with controlled execution and verification.

### Current Verified State

The real remediation implementation includes:

- `WindowsRemediationExecutor.cs`
- `ClearTempFilesAction.cs`
- `VerificationService.cs`
- `POST /api/autonomy/execute`

The currently implemented real action is:

clear_user_temp_files


The action operates on the current user's temporary directory and attempts to delete accessible temporary files while skipping locked or inaccessible files.
The real executor also fails safely for unsupported action IDs instead of attempting arbitrary execution.

### Important Safety Note
`clear_user_temp_files` is currently defined as:

- RiskLevel = Low
- IsReversible = false

Deleted temporary files cannot be restored by RigMD.
The action must therefore not be described as rollback-capable.
A partial cleanup is also possible.
For example:

- File A → deleted
- File B → deleted
- File C → locked and skipped

This is acceptable only if the result is reported accurately and no unrelated paths are modified.

### Current Verification
The current `VerificationService` checks the state of the temp directory after execution.
It currently considers the result resolved when:

- remaining files < 500
AND
- remaining size < 100 MB

This is useful as an initial heuristic, but stronger before/after evidence should eventually be tied to the exact remediation attempt.

### Remaining Safety Work
- [x] real Windows executor exists
- [x] `clear_user_temp_files` implementation exists
- [x] unsupported action IDs fail safely
- [x] locked files are skipped
- [x] post-action verification exists
- [ ] irreversible actions require explicit user confirmation
- [ ] verification should use stronger before/after evidence
- [ ] real execution needs more automated coverage
- [ ] manual safety validation should be documented

Phase 11 should remain partially complete until the remaining safety requirements are satisfied.

---

## Phase 12 — Verification, Rollback, and Pivot 🔲 PENDING

**Purpose:**  
Complete the closed-loop engine. After executing an action, the system must objectively measure whether the problem was resolved. If not, it must undo the action (if reversible) and try the next best candidate from the plan.

**Function:**  
Wire `IVerificationService`, `IRollbackManager`, and `IPivotEngine` into the `AutonomousOrchestrator`. The orchestrator now runs the full ACT → VERIFY → (if failed) ROLLBACK → PIVOT → NEXT ACTION loop.

**Key Files to Create:**
- `RigMD.Infrastructure/Remediation/RollbackManager.cs`
- `RigMD.Application/Services/Autonomy/PivotEngine.cs`
- Updated `AutonomousOrchestrator.cs` to run the full loop

**Acceptance Criteria:**
- [ ] If verification fails, rollback is attempted
- [ ] After rollback, PivotEngine selects the next action from the plan
- [ ] If all actions are exhausted, the system escalates (returns "Requires Human Intervention")
- [ ] All loop states (attempt, verify, rollback, pivot) are recorded to `RemediationRun` and `ActionAttempt` entities

---

## Phase 13 — History, Audit, & Recurring Patterns 🔲 PENDING

**Purpose:**  
Make all remediation activity visible and traceable. Past attempts must be persisted to SQLite and surfaced in the frontend History view. The system must also use history to improve future remediation ranking.

**Function:**  
Fully wire the SQLite repositories to the diagnostic and remediation flows. Persist every `DiagnosticSession`, `RemediationRun`, and `ActionAttempt` to the database. Surface this history in the frontend and use it to deprioritize actions that have historically failed.

**Key Files to Create/Update:**
- Repository implementations for `RemediationRun`, `ActionAttempt`, `VerificationResult`
- Updated `DiagnosticController` to read from SQLite instead of in-memory store
- `RecurringPatternService.cs` — detects repeated diagnoses across sessions

**Acceptance Criteria:**
- [ ] Sessions survive backend restarts (loaded from SQLite)
- [ ] History page shows all past sessions with remediation attempts
- [ ] Actions that failed in past sessions are ranked lower in future plans

---

## Phase 14 — AI Explanation Integration 🔲 PENDING

**Purpose:**  
Restore and constrain the AI explanation layer. The AI must only generate plain-language text that explains what the deterministic C# engine already decided. The AI must never influence which action to take, which Windows command to run, or whether a rollback is necessary.

**Function:**  
Integrate the Gemini API client (already stubbed as `OfflineAiExplainer`) with the real Gemini API for online explanations. Add a toggle that falls back to the offline explainer when no internet is available.

**Key Files to Create/Update:**
- `RigMD.Infrastructure/Ai/GeminiAiExplainer.cs` — real Gemini API implementation
- `RigMD.Infrastructure/Ai/OfflineAiExplainer.cs` — offline fallback (already exists)
- `appsettings.json` — add Gemini API key configuration (no secrets in source control)

**Acceptance Criteria:**
- [ ] With internet: AI explanation is fetched from Gemini and shown in the Diagnostic Result
- [ ] Without internet: Offline explainer generates a deterministic explanation
- [ ] AI output never contains a Windows command, registry path, or executable name
- [ ] API key is loaded from environment variable, never committed to Git

---

## Phase 15 — Optional Cloud Sync (Supabase) 🔲 PENDING

**Purpose:**  
Allow diagnostic data to optionally synchronize to the cloud for backup, multi-device access, and thesis data collection — without making cloud a dependency for core functionality.

**Function:**  
Implement a background worker that asynchronously syncs new SQLite records to Supabase. The worker must never block the main application flow. If Supabase is unreachable, the system continues operating normally.

**Key Files to Create:**
- `RigMD.Infrastructure/Sync/SupabaseSyncWorker.cs` — background sync hosted service
- `RigMD.Infrastructure/Sync/ICloudSyncService.cs` — sync interface

**Acceptance Criteria:**
- [ ] Disabling Supabase config causes zero errors and zero degraded functionality
- [ ] New sessions sync to Supabase within 60 seconds of being saved to SQLite
- [ ] Sync failures are logged but never surface as errors to the user

---

## Phase 16 — Desktop Packaging 🔲 PENDING

**Purpose:**  
Deliver RigMD as a proper standalone Windows desktop application. The user must be able to double-click an `.exe` and have everything start automatically — no terminal, no `npm run dev`, no `dotnet run`.

**Function:**  
Use WPF + WebView2 to create a thin shell that hosts the React frontend in a native window and manages the ASP.NET Core backend process as a child process. Package the entire thing as a single installable `.exe` or `.msi`.

**Key Files to Create:**
- `RigMD.Desktop/` — new WPF project
- `RigMD.Desktop/MainWindow.xaml` — WebView2 host
- `RigMD.Desktop/BackendManager.cs` — starts/stops the API process

**Acceptance Criteria:**
- [ ] Double-clicking the packaged executable opens the React UI in a native window
- [ ] The ASP.NET Core API starts automatically and binds to a free port
- [ ] The application closes cleanly with no orphaned processes

---

## Phase 17 — Performance, Testing, & Thesis Validation 🔲 PENDING

**Purpose:**  
Formally validate that the system meets the quantitative requirements defined in the thesis SRS before the final submission. This phase produces evidence — not just working code.

**Function:**  
Run automated performance benchmarks to measure startup time, diagnostic latency, and AI response time. Run the full autonomous test suite to validate diagnosis accuracy, safety rejection correctness, verification accuracy, rollback correctness, and pivot behavior.

**SRS Performance Requirements:**
- Startup time ≤ 5 seconds
- Rule-based diagnostic result ≤ 3 seconds
- AI explanation result ≤ 10 seconds

**Autonomous Evaluation Checklist:**
- [ ] Diagnostic accuracy: given known hardware states, correct category is diagnosed
- [ ] Safety rejection: high-risk actions are rejected on server OS
- [ ] Verification accuracy: resolved sessions are correctly detected as resolved
- [ ] Rollback correctness: failed actions are cleanly undone
- [ ] Pivot behavior: engine correctly moves to next action when current fails

**Key Files to Create:**
- `RigMD.Tests/Domain.Tests/DiagnosticEngineTests.cs`
- `RigMD.Tests/Application.Tests/AutonomousOrchestratorTests.cs`
- `RigMD.Tests/Application.Tests/SafetyPolicyTests.cs`
- `benchmarks/startup_benchmark.ps1`

---

## Current Status Summary

| Phase | Name | Status |
|---|---|---|
| 0 | Baseline & Architecture Alignment | ✅ Complete |
| 1 | Tag Legacy Code & Branching | ✅ Complete |
| 2 | Build the C#/.NET Foundation | ✅ Complete |
| 3 | Final Domain Model & ERD | ✅ Complete |
| 4 | Windows Observation Layer | ✅ Complete |
| 5 | SQLite + EF Core Persistence | 🔲 Pending |
| 6 | ASP.NET Core API | ✅ Complete |
| 7 | Connect the React Frontend | ✅ Complete |
| 8 | Migrate the Diagnostic Engine | ✅ Complete |
| 9 | Build the Autonomous Remediation Framework | ✅ Complete |
| 10 | Dry-Run Autonomous Engine | ✅ Complete |
| 11 | First Real Autonomous Remediation | 🟡 Partially Complete |
| 12 | Verification, Rollback, and Pivot | 🔲 Pending |
| 13 | History, Audit, & Recurring Patterns | 🔲 Pending |
| 14 | AI Explanation Integration | 🔲 Pending |
| 15 | Optional Cloud Sync | 🔲 Pending |
| 16 | Desktop Packaging | 🔲 Pending |
| 17 | Performance, Testing, & Thesis Validation | 🔲 Pending |

---

## Current Verified Test Status

As of the latest autonomy safety validation:

dotnet test
Total: 8
Passed: 8
Failed: 0
Skipped: 0


Current automated coverage includes:

- dry-run executor isolation
- real executor isolation
- rejected safety plan prevents execution
- Windows Server remediation rejection
- high-risk plan rejection
- empty plan rejection
- valid low-risk plan approval
- unsupported real remediation action failure

Automated coverage is still limited and must continue expanding before the legacy Python backend is retired.