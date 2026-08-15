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

## Phase 5 — SQLite + EF Core Persistence ✅ COMPLETE (Partially)

**Purpose:**  
Make the system 100% offline-capable. All data must be persisted locally to SQLite. Cloud (Supabase) is explicitly optional and cannot be required for the system to function.

**Function:**  
Define repository interfaces in the Application layer. Implement them with Entity Framework Core backed by SQLite in the Infrastructure layer. The Application layer never directly touches the database — it only calls repository interfaces.

**Key Files:**
- `RigMD.Application/Contracts/Repositories/IProfileRepository.cs` — save/load system profiles
- `RigMD.Application/Contracts/Repositories/IDiagnosticSessionRepository.cs` — save/load sessions
- `RigMD.Infrastructure/Persistence/RigMdDbContext.cs` — EF Core DbContext
- `RigMD.Infrastructure/Persistence/Repositories/*.cs` — concrete repository implementations

**Note:** As of Phase 10, full repository wiring to all endpoints is a known in-progress item. Session data is currently held in memory between requests.

**Acceptance Criteria:**
- [ ] EF Core migration runs successfully and creates the SQLite file
- [ ] DiagnosticSession records are saved to SQLite after each diagnosis
- [ ] Sessions survive a backend restart (in-memory is NOT sufficient)

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

**Purpose:**  
Define the contracts (interfaces) that govern the entire autonomous system before implementing any of it. This enforces a clean separation between "what the system must do" (interfaces) and "how it does it" (implementations). It also allows Dry-Run mode to substitute real execution with a simulation.

**Function:**  
Create C# interfaces in the Application layer for every step of the autonomous loop. No Windows commands are written yet — only the contracts.

**Key Files (all in `RigMD.Application/Contracts/Autonomy/`):**
- `IRemediationRegistry.cs` — catalog of officially approved actions; executor may ONLY run registered actions
- `IRemediationPlanner.cs` — takes `DiagnosticOutput`, formulates a `RemediationPlan`
- `ISafetyPolicy.cs` — evaluates a plan against the current system state; approves, rejects, or requires consent
- `IRemediationExecutor.cs` — executes one specific action (implemented as Dry-Run or Real in later phases)
- `IVerificationService.cs` — measures whether the action resolved the problem
- `IRollbackManager.cs` — reverses a failed or harmful action
- `IPivotEngine.cs` — decides the next step when an action fails (try next action, escalate, or stop)

**Key Models (in `RigMD.Application/Models/AutonomyModels.cs`):**
- `RemediationActionDef` — action descriptor with ID, name, category, risk level, reversibility
- `RemediationPlan` — ordered list of planned actions with strategy reasoning
- `SafetyEvaluation` — approval status, rejection reason, and warnings
- `ExecutionResult` / `ExecutionProof` — result of an execution attempt with before/after proof
- `VerificationStatus` enum — `Resolved`, `Unresolved`, `Worse`, `Unknown`

**Acceptance Criteria:**
- [x] All interfaces compile cleanly with 0 errors
- [x] Models are sufficient to represent the entire autonomous loop state

---

## Phase 10 — Dry-Run Autonomous Engine ✅ COMPLETE

**Purpose:**  
Wire together all Phase 9 interfaces with concrete implementations — but use a safe "Dry Run" executor that simulates actions without touching the system. This allows the entire autonomous loop to be tested and demonstrated before any real Windows changes are made.

**Function:**  
Implement all autonomy interfaces. The `AutonomousOrchestrator` runs the full pipeline: Plan → Safety Check → Execute (dry run). Expose a `POST /api/autonomy/dry-run` endpoint to trigger and inspect the loop from Postman or the frontend.

**Key Files (all in `RigMD.Application/Services/Autonomy/`):**
- `RemediationRegistry.cs` — the concrete action catalog (10 approved Windows actions)
- `RemediationPlanner.cs` — maps a diagnosed category to matching registry actions
- `SafetyPolicy.cs` — rejects high-risk actions on server OS; flags actions needing consent
- `DryRunRemediationExecutor.cs` — simulates execution, returns "DRY RUN: Simulated..." result
- `AutonomousOrchestrator.cs` — coordinates the full Plan → SafetyCheck → Execute loop

**New Interface:**
- `IAutonomousOrchestrator.cs` / `OrchestrationResult` — the top-level orchestrator contract

**New API Endpoint:**
- `POST /api/autonomy/dry-run` — trigger the orchestrator with a diagnosed category; returns full trace

**How to Test (Postman):**
```
POST http://localhost:5273/api/autonomy/dry-run
Content-Type: application/json

{
  "DiagnosedCategory": "OS performance degradation"
}
```

**Acceptance Criteria:**
- [x] `dotnet build` succeeds with 0 errors
- [x] Dry-run endpoint returns a complete JSON trace showing Plan, SafetyCheck, and Execution
- [x] Different input categories produce different plans

---

## Phase 11 — First Real Autonomous Remediation 🔲 PENDING

**Purpose:**  
Move from simulated execution to a single real, narrowly-scoped, low-risk, reversible Windows action. This is the first time the system will make an actual change to the PC — with full verification and rollback capability.

**Function:**  
Select one action from the registry (e.g., `clear_user_temp_files`) and implement it as a real `WindowsRemediationExecutor`. Add a concrete `VerificationService` that measures the disk space before and after. Implement rollback (if any files can be recovered).

**Key Files to Create:**
- `RigMD.Infrastructure/Remediation/WindowsRemediationExecutor.cs` — replaces `DryRunRemediationExecutor` for real execution
- `RigMD.Infrastructure/Remediation/Actions/ClearTempFilesAction.cs` — the one real action
- `RigMD.Infrastructure/Remediation/VerificationService.cs` — measures disk free space delta

**Acceptance Criteria:**
- [ ] Executing `clear_user_temp_files` deletes files from `%TEMP%`
- [ ] `VerificationService` measures disk space before and after and reports the delta
- [ ] If the action fails mid-way, no partial state is left
- [ ] Safety Policy rejects the action if the system is a server OS

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
| 5 | SQLite + EF Core Persistence | 🟡 Partially Complete |
| 6 | ASP.NET Core API | ✅ Complete |
| 7 | Connect the React Frontend | ✅ Complete |
| 8 | Migrate the Diagnostic Engine | ✅ Complete |
| 9 | Build the Autonomous Remediation Framework | ✅ Complete |
| 10 | Dry-Run Autonomous Engine | ✅ Complete |
| 11 | First Real Autonomous Remediation | 🔲 Pending |
| 12 | Verification, Rollback, and Pivot | 🔲 Pending |
| 13 | History, Audit, & Recurring Patterns | 🔲 Pending |
| 14 | AI Explanation Integration | 🔲 Pending |
| 15 | Optional Cloud Sync (Supabase) | 🔲 Pending |
| 16 | Desktop Packaging | 🔲 Pending |
| 17 | Performance, Testing, & Thesis Validation | 🔲 Pending |
