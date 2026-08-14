# Migration Matrix

> Tracks the migration status of every existing legacy capability to the new C#/.NET architecture.  
> Update this document as migration work progresses.

**Legend**:
- ✅ `DONE` — Migrated and verified in C#
- `[/]` `IN PROGRESS` — Actively being migrated
- `[ ]` `PENDING` — Not yet started
- `[N]` `NEW` — Does not exist in legacy; entirely new capability
- `[D]` `DISCARD` — Intentionally not migrating (Python-specific artifact or superseded)
- `[R]` `REDESIGN` — Migrating concept but not file/structure (meaningful refactor required)

---

## 1. Backend: Hardware / Windows Observation

| Legacy (Python) | Migration Status | Target (C#) | Notes |
|---|---|---|---|
| `routers/hardware.py` — CPU detection | `[ ]` PENDING | `Infrastructure/Windows/CpuInformationProvider.cs` | Replace `wmi`/`psutil` with `System.Management` |
| `routers/hardware.py` — GPU + driver | `[ ]` PENDING | `Infrastructure/Windows/GpuInformationProvider.cs` | |
| `routers/hardware.py` — RAM detection | `[ ]` PENDING | `Infrastructure/Windows/MemoryInformationProvider.cs` | |
| `routers/hardware.py` — OS version | `[ ]` PENDING | `Infrastructure/Windows/OperatingSystemInformationProvider.cs` | Use `Registry` / `Environment.OSVersion` |
| `routers/hardware.py` — Storage classification | `[ ]` PENDING | `Infrastructure/Windows/StorageInformationProvider.cs` | Preserve NVMe/SSD/HDD classification logic |
| `routers/hardware.py` — Physical + logical disk | `[ ]` PENDING | `Infrastructure/Windows/StorageInformationProvider.cs` | |
| `routers/hardware.py` — Driver info (chipset) | `[ ]` PENDING | `Infrastructure/Windows/DriverInformationProvider.cs` | |
| `routers/hardware.py` — Browser/process detection | `[ ]` PENDING | `Infrastructure/Windows/ProcessInformationProvider.cs` | |
| `routers/hardware.py` — Game/heavy process detection | `[ ]` PENDING | `Infrastructure/Windows/ProcessInformationProvider.cs` | |
| `routers/hardware.py` — System age | `[ ]` PENDING | `Infrastructure/Windows/OperatingSystemInformationProvider.cs` | |
| `GET /api/hardware/live` | `[ ]` PENDING | `GET /api/system-profile/live` | Endpoint name changes |

---

## 2. Backend: Diagnostic Engine

| Legacy (Python) | Migration Status | Target (C#) | Notes |
|---|---|---|---|
| `services/diagnostic_engine.py` — symptom interpretation | `[ ]` PENDING | `Domain/Rules/` + `Application/Services/DiagnosticRuleService.cs` | **[R] REDESIGN** — Decompose monolith |
| `services/diagnostic_engine.py` — category selection | `[ ]` PENDING | `Application/Services/DiagnosticRuleService.cs` | |
| `services/diagnostic_engine.py` — scoring/evidence | `[ ]` PENDING | `Application/Services/ScoringService.cs` + `EvidenceService.cs` | |
| `services/diagnostic_engine.py` — confidence logic | `[ ]` PENDING | `Application/Services/ConfidenceService.cs` | |
| `services/diagnostic_engine.py` — "no active issue" handling | `[ ]` PENDING | `Application/Services/DiagnosticRuleService.cs` | |
| `services/diagnostic_engine.py` — recommended next step | `[ ]` PENDING | `Application/Services/AdvisoryActionService.cs` | |
| `services/diagnostic_engine.py` — verification target logic | `[ ]` PENDING | `Application/Autonomous/IVerificationService.cs` | Seed for autonomous verification |
| `services/ai_explainer.py` | `[ ]` PENDING | `Infrastructure/Ai/GeminiExplainerClient.cs` | Port Gemini API client via `HttpClient` |

---

## 3. Backend: Resolution / Recheck

| Legacy (Python) | Migration Status | Target (C#) | Notes |
|---|---|---|---|
| `services/resolution_service.py` | `[ ]` PENDING | `Application/Services/ResolutionService.cs` | Seed for formal `VerificationResult` |
| `POST /api/diagnosis/{id}/needs-recheck` | `[ ]` PENDING | `POST /api/diagnostics/{id}/needs-recheck` | |
| `POST /api/diagnosis/{id}/check-resolution` | `[ ]` PENDING | `POST /api/diagnostics/{id}/check-resolution` | |

---

## 4. Backend: Remediation

| Legacy (Python) | Migration Status | Target (C#) | Notes |
|---|---|---|---|
| `routers/remediation.py` (stub) | `[R]` REDESIGN | `Application/Autonomous/` + `Infrastructure/Remediation/` | Full redesign; the stub is discarded, the concept is carried forward |
| `services/remediation_service.py` | `[R]` REDESIGN | `Application/Autonomous/IRemediationPlanner.cs` + `ISafetyPolicy.cs` + `IRemediationRegistry.cs` | Decompose into proper framework |

---

## 5. Backend: Data Persistence

| Legacy (Python) | Migration Status | Target (C#) | Notes |
|---|---|---|---|
| `Profile` model (`profiles` table) | `[ ]` PENDING | `Domain/Entities/SystemProfile.cs` + EF Core migration | Rename to `SystemProfile` for clarity |
| `Session` model (`sessions` table) | `[ ]` PENDING | `Domain/Entities/DiagnosticSession.cs` + EF Core migration | Resolution fields preserved |
| `Recommendation` model (`recommendations` table) | `[R]` REDESIGN | `Domain/Entities/OutputWarningSign.cs` + `WarningSign.cs` | Align with SDD ERD; current flat model is too simple |
| `warning_signs` (flat text in session) | `[R]` REDESIGN | `Domain/Entities/OutputWarningSign.cs` | Promote to proper entity |
| `resolution_status` / `resolution_proof` fields | `[ ]` PENDING | Part of `DiagnosticSession` entity + `RemediationRun` | Preserve existing logic; eventually links to formal `VerificationResult` |
| Supabase / PostgreSQL | `[D]` DISCARD (for now) | SQLite via EF Core (source of truth) + optional Supabase sync worker later | |

---

## 6. Backend: Routers / API Endpoints

| Legacy Endpoint | Migration Status | Target C# Endpoint | Notes |
|---|---|---|---|
| `GET /` (root health) | `[ ]` PENDING | `GET /api/health` | |
| `POST /api/diagnosis/submit` | `[ ]` PENDING | `POST /api/diagnostics` | |
| `GET /api/hardware/live` (in hardware router) | `[ ]` PENDING | `GET /api/system-profile/live` | |
| `GET /api/dashboard/...` | `[ ]` PENDING | `GET /api/dashboard/...` | |
| `GET /api/history/...` | `[ ]` PENDING | `GET /api/history/...` | |
| `GET /api/recurring/...` | `[ ]` PENDING | `GET /api/recurring/...` | |
| `GET /api/warning-signs/...` | `[ ]` PENDING | `GET /api/warning-signs/...` | |
| `GET/PUT /api/profile/...` | `[ ]` PENDING | `GET/PUT /api/system-profile/...` | |
| `POST /api/diagnosis/{id}/needs-recheck` | `[ ]` PENDING | `POST /api/diagnostics/{id}/needs-recheck` | |
| `POST /api/diagnosis/{id}/check-resolution` | `[ ]` PENDING | `POST /api/diagnostics/{id}/check-resolution` | |
| `POST /api/remediation/...` (stub) | `[R]` REDESIGN | `POST /api/remediation/preview`, `POST /api/remediation/execute` | Build only after framework is ready |

---

## 7. New Entities (No Legacy Equivalent)

| New Entity | Target Location | Priority | Notes |
|---|---|---|---|
| `SessionAnswer` | `Domain/Entities/SessionAnswer.cs` | High | Symptom intake answers as first-class entity (currently flat columns in `Session`) |
| `DiagnosticOutput` | `Domain/Entities/DiagnosticOutput.cs` | High | Formal output entity (currently inlined in session) |
| `ReasoningFactor` | `Domain/Entities/ReasoningFactor.cs` | High | Evidence/reasoning breakdown |
| `WarningSign` | `Domain/Entities/WarningSign.cs` | High | Reference warning sign catalog |
| `OutputWarningSign` | `Domain/Entities/OutputWarningSign.cs` | High | Junction entity: output $\leftrightarrow$ warning sign |
| `Report` | `Domain/Entities/Report.cs` | Medium | For PDF/print export (promised by SRS) |
| `RemediationRun` | `Domain/Entities/RemediationRun.cs` | Medium | Autonomous remediation run record |
| `ActionAttempt` | `Domain/Entities/ActionAttempt.cs` | Medium | Individual action within a RemediationRun |
| `VerificationResult` | `Domain/Entities/VerificationResult.cs` | Medium | Outcome of verification after action |
| `RollbackEvent` | `Domain/Entities/RollbackEvent.cs` | Medium | Record of a rollback operation |
| `PivotEvent` | `Domain/Entities/PivotEvent.cs` | Medium | Record of a pivot to next action |

---

## 8. Frontend Migration

| Legacy Component | Migration Status | Target | Notes |
|---|---|---|---|
| `SystemProfileView.tsx` | `[ ]` PENDING | Keep, reconnect to `GET /api/system-profile/live` | First vertical slice target |
| `NewDiagnosisView.tsx` | `[ ]` PENDING | Keep, reconnect to `POST /api/diagnostics` | |
| `DiagnosticIntakeReview.tsx` | `[ ]` PENDING | Keep, no API changes | |
| `DiagnosticResultView.tsx` | `[ ]` PENDING | Keep, minor data contract changes expected | |
| `DiagnosticHistoryView.tsx` | `[ ]` PENDING | Keep, reconnect to `GET /api/history` | |
| `DiagnosticSessionDetailView.tsx` | `[ ]` PENDING | Keep, reconnect to `GET /api/history/{id}` | |
| `RecurringPatternsView.tsx` | `[ ]` PENDING | Keep, reconnect to `GET /api/recurring` | |
| `WarningSignsView.tsx` | `[ ]` PENDING | Keep, reconnect to `GET /api/warning-signs` | |
| `HardwareDashboard.tsx` | `[ ]` PENDING | Keep, reconnect to `GET /api/system-profile/live` | |
| `HelpScopeView.tsx` | `[ ]` PENDING | Keep, no backend dependency | |
| `ReportsView.tsx` | `[N]` NEW | Implement from scratch | Empty file in legacy |
| `AppSidebar.tsx` | `[ ]` PENDING | Keep, minimal changes expected | |
| `TopHeader.tsx` | `[ ]` PENDING | Keep, minimal changes expected | |
| `HardwareCard.tsx` | `[N]` NEW | Implement from scratch | Empty file in legacy |
| `profileService.ts` | `[ ]` PENDING | Keep, update base URL | |
| Inline API calls | `[R]` REDESIGN | Abstract into dedicated service files | Improve separation of concerns |
| Vite API base URL | `[ ]` PENDING | Add `VITE_API_URL` env variable pointing to C# API | Do not hard-switch until C# endpoint is verified |
