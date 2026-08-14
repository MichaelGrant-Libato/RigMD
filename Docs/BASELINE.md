# Baseline Reference

> This document captures the known state of the Python/FastAPI baseline implementation at the point the C# migration begins.  
> Reference this alongside the legacy source code when verifying behavioral parity during the migration.

---

## 1. Baseline Tag

```
git tag: v0.1-python-baseline
branch at tag: feature/csharp-migration (branched from main)
```

---

## 2. Known Working Capabilities

The following capabilities were confirmed working in the Python/FastAPI baseline:

### 2.1 Hardware Detection
- CPU model, speed, core and thread count via WMI
- GPU model and driver version via WMI
- RAM total GB via `psutil`
- OS version (edition, build number) via WMI / registry
- Storage type classification: NVMe, SSD, HDD
- Physical disk metadata (model, size, bus type)
- Logical disk mapping and disk usage via `psutil`
- Browser processes detected: Chrome, Firefox, Edge, Opera
- Heavy process / game detection
- Chipset driver information via WMI
- System age estimation

### 2.2 Diagnostic Intake
- Eight-step structured symptom intake:
  1. Symptom Type
  2. Affected Activity
  3. Frequency
  4. Severity
  5. Duration
  6. Recent Changes
  7. System State
  8. Warning Signs

### 2.3 Diagnostic Engine
- Symptom-to-category interpretation
- Rule-based scoring and evidence collection
- Confidence label computation (Low / Medium / High / Critical)
- Action category determination: Monitor / Maintain / Troubleshoot / Escalate
- "No active issue" detection path
- Recommended next steps
- Verification target suggestions
- Gemini AI explanation generation (constrained to diagnostic context)

### 2.4 Persistence
- Profile upsert based on hardware fingerprint (CPU + RAM + Storage + OS)
- Diagnostic session creation with all eight intake fields
- Resolution lifecycle fields: `resolution_status`, `resolution_checked_at`, `resolution_summary`, `resolution_proof`, `last_action_status`, `last_action_summary`

### 2.5 Resolution / Recheck
- Mark session as `needs_recheck` after action
- Re-run hardware check + resolution evaluation against original diagnosis
- Update session resolution state in-place

### 2.6 History and Patterns
- Paginated diagnostic session history
- Recurring flag detection across sessions
- Warning sign aggregation
- Per-session detail view

---

## 3. Known Limitations / Technical Debt

| Area | Issue |
|---|---|
| Hardware detection | Monolithic function in `routers/hardware.py`. No unit testing possible without real WMI. |
| Diagnostic engine | Single large file (`diagnostic_engine.py`, 34 KB). Scoring, evidence, and action logic are coupled. |
| Session model | Flat columns for warning signs and symptom answers rather than relational entities. |
| Recommendation model | Loosely coupled to sessions; lacks proper reasoning factor separation. |
| Frontend service layer | Only `profileService.ts` abstracted; all other API calls are inline in page components. |
| Remediation | `remediation_service.py` (20 KB) and `routers/remediation.py` exist but are early-stage and not integrated into the full pipeline. |
| Resolution loop | Functional but not yet a formalized closed-loop (no `ActionAttempt` / `VerificationResult` / `RollbackEvent` tracking). |
| Cloud dependency | `session_model.py` uses PostgreSQL-specific `UUID` dialect and `JSONB`; requires Supabase connection for any session save. |
| No offline mode | System fails entirely if the database connection is unavailable. |
| `ReportsView.tsx` | Empty file; PDF/print export not implemented. |
| `HardwareCard.tsx` | Empty file; component not implemented. |

---

## 4. Diagnostic Engine: Known Category Mappings

The following primary diagnostic categories are known to exist in the engine (from `diagnostic_engine.py`):

- OS Performance Degradation
- Driver Conflict
- Storage Health Behavior
- Boot / Startup Failure
- Thermal Condition
- Display Driver Behavior
- System Log Event Flags

**C# migration requirement**: The C# `DiagnosticRuleService` must reproduce each category's scoring logic and produce an identical result for the same input. Any intentional deviation must be explicitly documented.

---

## 5. API Contracts Reference (Legacy)

Base URL: `http://localhost:8000`

| Method | Path | Purpose |
|---|---|---|
| GET | `/` | Health check |
| GET | `/api/hardware/live` | Live hardware profile |
| POST | `/api/diagnosis/submit` | Submit symptoms + collect hardware + diagnose + save |
| POST | `/api/diagnosis/{id}/needs-recheck` | Mark as needing recheck |
| POST | `/api/diagnosis/{id}/check-resolution` | Re-evaluate resolution status |
| GET | `/api/dashboard/...` | Aggregated dashboard data |
| GET | `/api/history/...` | Paginated session list |
| GET | `/api/history/{id}` | Session detail |
| DELETE | `/api/history/{id}` | Delete session |
| GET | `/api/recurring/...` | Recurring pattern data |
| GET | `/api/warning-signs/...` | Warning sign data |
| GET | `/api/profile/...` | Profile retrieval |
| PUT | `/api/profile/...` | Profile update |
| GET/POST | `/api/remediation/...` | Remediation (stub) |

---

## 6. React Frontend Entry Points (Legacy)

Base URL: `http://localhost:5173`

| Route | Component | Backend Dependency |
|---|---|---|
| `/system-profile` | `SystemProfileView.tsx` | `GET /api/hardware/live` |
| `/new-diagnosis` | `NewDiagnosisView.tsx` | `POST /api/diagnosis/submit` |
| `/result` | `DiagnosticResultView.tsx` | Receives result from submission |
| `/history` | `DiagnosticHistoryView.tsx` | `GET /api/history/...` |
| `/history/:id` | `DiagnosticSessionDetailView.tsx` | `GET /api/history/{id}` |
| `/recurring` | `RecurringPatternsView.tsx` | `GET /api/recurring/...` |
| `/warning-signs` | `WarningSignsView.tsx` | `GET /api/warning-signs/...` |
| `/dashboard` | `HardwareDashboard.tsx` | `GET /api/hardware/live`, `GET /api/dashboard/...` |
| `/help` | `HelpScopeView.tsx` | None |
| `/reports` | `ReportsView.tsx` | Not implemented |

---

## 7. Environment Variables (Legacy)

| Variable | Example | Used By |
|---|---|---|
| `DATABASE_URL` | `postgresql://...` | SQLAlchemy connection (Supabase) |
| `GEMINI_API_KEY` | `...` | AI explainer service |
| `FRONTEND_URL` | `http://localhost:5173` | CORS origin whitelist |
