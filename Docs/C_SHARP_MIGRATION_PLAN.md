# C# Migration & Autonomous Engine Roadmap

This document outlines the comprehensive 17-phase roadmap for migrating RigMD from a Python advisory system to a C#/.NET autonomous diagnostic and remediation system.

## The Paradigm Shift

**OLD:**
- Python + CustomTkinter (or FastAPI)
- SQLite
- Advisory Output

**NEW:**
- React + C#/.NET
- SQLite local source of truth
- Controlled Autonomous Core
- Optional Supabase synchronization

### Architecture Flow
```text
                 ┌───────────────┐
                 │   React UI    │
                 └───────┬───────┘
                         ↓
                 ┌───────────────┐
                 │   C# / .NET   │
                 │   RigMD Core  │
                 └───────┬───────┘
                         │
        ┌────────────────┼────────────────┐
        ↓                ↓                ↓
    Windows          SQLite            AI
   Integration      LOCAL STATE      Explanation
                         │
                         ↓
                  OPTIONAL SYNC
                         │
                         ↓
                     Supabase
```

---

## Roadmap Phases

### Phase 0 — Baseline & Architecture Alignment ✅ COMPLETE
- Establish target architecture documentation (`ARCHITECTURE.md`, `AUTONOMOUS_ENGINE.md`, `REMEDIATION_POLICY.md`).
- Ensure team alignment on the shift from advisory DSS to autonomous DSS.

Phase 0 is complete only when:
- [x] Final target architecture documented
- [x] Migration architecture documented
- [x] Autonomous engine architecture documented
- [x] Remediation safety policy documented
- [x] Old vs new responsibilities mapped
- [x] Final domain concepts identified (Completed via Phase 3 ERD generation)
- [x] SRS/SDD impact identified (Recorded in README_V2.md)
- [x] Scope changes explicitly recorded (Recorded in DECISIONS.md and README_V2.md)
- [x] Team agrees on SQLite vs Supabase responsibilities

### Phase 1 — Tag Legacy Code & Branching ✅ COMPLETE
- [x] Tagged Python baseline: `git tag v0.1-python-baseline` on commit `bde24f5`.
- [x] Active branch: `feature/csharp-migration`.
- [x] Baseline documentation complete: `BASELINE.md`, `LEGACY_CAPABILITY_INVENTORY.md`, `MIGRATION_MATRIX.md`.

### Phase 2 — Build the C#/.NET Foundation ✅ COMPLETE
- [x] Create the `backend-dotnet/` directory alongside the legacy `backend/`. (Do not overwrite the Python backend yet).
- [x] Initialize the solution: `RigMD.sln`, `RigMD.Api`, `RigMD.Application`, `RigMD.Domain`, `RigMD.Infrastructure`, and `RigMD.Tests`.

### Phase 3 — Final Domain Model & ERD ✅ COMPLETE
- [x] Reconcile the SDD's existing conceptual ERD with the new autonomous concepts. Do not simplify or discard existing entities merely because the current Python implementation is flatter.
- [x] Define exact C# entity classes for `SystemProfile`, `DiagnosticSession`, `SessionAnswer`, `DiagnosticOutput`, `ReasoningFactor`, `WarningSign`, `OutputWarningSign`.
- [x] Define explicit C# entity classes for the autonomous pipeline: `RemediationRun`, `ActionAttempt`, `VerificationResult`, `RollbackEvent`, `PivotEvent`.

### Phase 4 — Windows Observation Layer ✅ COMPLETE
- [x] Build isolated information providers instead of a monolithic scanner.
- [x] Create isolated providers: `CpuProvider`, `GpuProvider`, `StorageProvider`, `MemoryProvider`, `MotherboardProvider`, `ProcessProvider`, and `OperatingSystemProvider`.

### Phase 5 — SQLite + EF Core Persistence
- Define repository/service abstractions at the Application boundary (`IProfileRepository`, `IDiagnosticSessionRepository`, `IRemediationRepository`).
- Implement them through EF Core in Infrastructure (`ProfileRepository`, `DiagnosticSessionRepository`, `RemediationRepository`). This prevents the domain layer from becoming tied to persistence concerns.

### Phase 6 — ASP.NET Core API
- Establish API routes: `/api/health`, `/api/system-profile/live`, `/api/system-profile`, `/api/diagnostics`, `/api/history`.
- Do not implement remediation execution endpoints yet. Establish the API structure first.

### Phase 7 — Connect the React Frontend
- Introduce a configurable API base URL (`VITE_API_URL=http://localhost:xxxx`) and connect the existing React UI to the C# backend only after the corresponding C# endpoints are independently verified.

### Phase 8 — Migrate the Diagnostic Engine ✅ COMPLETE
- [x] Rebuild the intellectual core in C#.
- [x] Port `diagnostic_engine.py` logic (Scoring, Evidence, Confidence).
- [x] Translate the Python rule definitions (JSON/Dicts) into C# domain structures or a local SQLite rules table.
- [x] Implement `DiagnosticRuleService.cs` and `ScoringService.cs`.
- [x] Ensure the outcome perfectly matches the legacy Python logic for the same inputs.

### Phase 9 — Build the Autonomous Remediation Framework
- Build interfaces/models for autonomy without writing actual Windows tweaks yet.
- Create: `IRemediationPlanner`, `ISafetyPolicy`, `IRemediationExecutor`, `IVerificationService`, `IRollbackManager`, `IPivotEngine`, and an explicit `IRemediationRegistry` (executor only executes officially registered actions).

### Phase 10 — Dry-Run Autonomous Engine
- Bridge the diagnostic system to the autonomous system using Dry Run mode (a permanent developer/test mode).
- Pipeline: DIAGNOSE -> PLAN -> SAFETY CHECK -> DRY RUN.

### Phase 11 — First Real Autonomous Remediation
- Implement exactly ONE narrowly bounded, low-risk, reversible or recoverable, measurable action selected after evaluating actual supported Windows capabilities (based on preconditions + measurable verification).

### Phase 12 — Verification, Rollback, and Pivot
- Formalize the closed-loop engine: ACT -> VERIFY -> (if failed) ROLLBACK -> PIVOT -> NEXT SAFE ACTION.

### Phase 13 — History, Audit, & Recurring Patterns
- Expand history to audit trails of failed/successful remediation attempts.
- Use history to influence ranking/prioritization. (Note: AI does not change its own rules; the approved remediation catalog defines allowed actions).

### Phase 14 — AI Explanation Integration
- The deterministic C# engine decides what action to take, safety risks, execution, and rollback.
- AI handles strictly plain-language explanation, summarization, and presentation. AI must never decide which Windows commands to execute.

### Phase 15 — Optional Cloud Sync (Supabase)
- Optional asynchronous SQLite -> Supabase synchronization. Supabase is not mandatory for core functionality.

### Phase 16 — Desktop Packaging
- Package the React UI and C# API into a unified Windows application using a WPF + WebView2 shell.

### Phase 17 — Performance, Testing, & Thesis Validation
- Validate against SRS constraints: Startup <= 5s, Rule-based result <= 3s, AI result <= 10s.
- Add Autonomous Remediation Evaluation: test diagnosis accuracy, safe action rejection, verification accuracy, rollback correctness, and pivot behavior.

---

## Immediate Next Steps

1. Tag current Python implementation.
2. Create migration branch.
3. Finalize architecture/migration documentation.
4. Define final domain model + ERD.
5. Create backend-dotnet solution.
6. Implement Windows observation interfaces/providers.
7. Implement SystemProfile domain/application flow.
8. Implement SQLite persistence.
9. Build `/api/health`.
10. Build `/api/system-profile/live`.
11. Verify actual Windows data.
12. Connect React System Profile screen.
