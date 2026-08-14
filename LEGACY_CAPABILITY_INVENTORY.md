# Legacy Capability Inventory

> This document provides an accurate, file-by-file inventory of what the legacy Python/FastAPI backend and React frontend currently implement.  
> Use this as the authoritative reference when deciding what to migrate, rewrite, discard, or preserve during the C# migration.

---

## 1. Backend — Python / FastAPI (`backend/`)

### 1.1 Entry Point & Orchestration

**`backend/main.py`** — *Primary orchestration layer*
- Registers all routers: `hardware`, `dashboard`, `history`, `recurring`, `warning_signs`, `profile`, `remediation`.
- Orchestrates the full diagnosis submission pipeline inline:
  - Calls `get_live_hardware_stats()` from the hardware router.
  - Calls `run_diagnostic()` from the diagnostic engine.
  - Calls `get_or_create_live_profile()` to upsert a hardware profile.
  - Calls `save_diagnostic_session()` to persist the result.
- **Key endpoints in `main.py`**:
  - `POST /api/diagnosis/submit` — Full diagnostic intake + hardware collection + save.
  - `POST /api/diagnosis/{session_id}/needs-recheck` — Marks a session as needing re-evaluation.
  - `POST /api/diagnosis/{session_id}/check-resolution` — Re-collects live hardware, runs resolution check, updates session.

---

### 1.2 Routers (`backend/routers/`)

| File | Prefix / Endpoints | Key Capabilities |
|---|---|---|
| `hardware.py` | `/api/hardware/` | Live hardware stats (CPU, GPU, RAM, OS, Storage, Drivers, Browser processes, Games, System age). Contains `get_live_hardware_stats()` used by other modules. |
| `dashboard.py` | `/api/dashboard/` | Aggregated overview: recent sessions, recurring flag counts, warning sign summary. |
| `history.py` | `/api/history/` | Paginated diagnostic session list, session detail, delete session. |
| `recurring.py` | `/api/recurring/` | Recurring pattern detection across sessions: symptoms, causes, warning signs. |
| `warning_signs.py` | `/api/warning-signs/` | Warning sign records lookup, per-session and aggregate. |
| `remediation.py` | `/api/remediation/` | Stub/early implementation for remediation operations. |
| `profile.py` | `/api/profile/` | Profile CRUD: get current profile, update profile fields. |

---

### 1.3 Services (`backend/services/`)

| File | Key Capabilities |
|---|---|
| `diagnostic_engine.py` (34 KB) | Core rule-based diagnostic logic. Symptom interpretation, category selection, scoring/evidence ranking, confidence computation, AI explanation invocation, recommended next steps, verification target logic, "no active issue" handling. |
| `remediation_service.py` (20 KB) | Remediation-related logic. Suggests remediation steps, tracks action state. |
| `resolution_service.py` | Re-evaluates a previously diagnosed session against live hardware to determine if the issue has been resolved. |
| `ai_explainer.py` (9 KB) | Gemini API client. Constructs structured prompts from diagnostic context and returns plain-language explanations. |
| `session_store.py` (8 KB) | Helpers for querying, filtering, and aggregating diagnostic session records. |

---

### 1.4 Models (`backend/models/`)

**`profile_model.py`** — `Profile` table
| Field | Type | Notes |
|---|---|---|
| `id` | UUID | Primary key |
| `cpu_model` | String | |
| `ram_capacity` | String | e.g. "16 GB" |
| `storage_type` | String | e.g. SSD, HDD, NVMe |
| `storage_capacity` | String | e.g. "512 GB" |
| `storage_details` | JSONB | Per-drive detail from live hardware scan |
| `os_version` | String | e.g. "Windows 11 23H2" |
| `gpu_driver` | String | e.g. "556.12" |
| `chipset_driver` | String | |
| `system_age` | String | e.g. "2 years" |
| `created_at` | DateTime | Auto-set |

**`session_model.py`** — `Session` table (Diagnostic Session)
| Field | Type | Notes |
|---|---|---|
| `id` | UUID | Primary key |
| `profile_id` | UUID FK | Links to Profile |
| `symptom_type` | String | Structured intake field |
| `affected_activity` | String | |
| `frequency` | String | |
| `severity` | String | |
| `duration` | String | |
| `recent_changes` | Text | |
| `system_state` | String | |
| `warning_signs` | Text | |
| `diagnosed_category` | String | Diagnostic engine output |
| `action_category` | String | e.g. Monitor, Maintain, Troubleshoot, Escalate |
| `confidence_label` | String | |
| `ai_explanation` | Text | Gemini-generated plain language |
| `is_recurring` | Boolean | |
| `created_at` | DateTime | |
| `resolution_status` | String | "open", "needs_recheck", "resolved" |
| `resolution_checked_at` | DateTime | |
| `resolution_summary` | Text | |
| `resolution_proof` | JSON | Evidence array |
| `last_action_status` | String | |
| `last_action_summary` | Text | |

**`recommendation_model.py`** — `Recommendation` table
| Field | Type | Notes |
|---|---|---|
| `id` | UUID | |
| `session_id` | UUID FK | Links to Session |
| `warning_sign` | String | |
| `threshold` | String | |
| `recommended_action` | Text | |
| `created_at` | DateTime | |

---

### 1.5 Schemas (`backend/schemas/`)

| File | Purpose |
|---|---|
| `diagnosis_schema.py` | Pydantic request/response models for diagnosis submission. |
| `profile_schema.py` | Pydantic request/response models for profile operations. |

---

### 1.6 Windows / Hardware Detection

All hardware detection lives inside **`backend/routers/hardware.py`** (19 KB).

Capabilities confirmed:
- CPU model, speed, core/thread count
- GPU model + driver version
- RAM total GB
- OS version (edition, build)
- Storage type classification (NVMe, SSD, HDD)
- Physical disk metadata
- Logical disk mapping + disk usage
- Browser process detection
- Game/heavy process detection
- System age estimation
- Fallbacks/error paths for WMI failures

Implementation basis: Python `wmi`, `psutil`, and standard library.

---

## 2. Frontend — React / Vite (`frontend/src/`)

### 2.1 Pages (`frontend/src/pages/`)

| File | Status | Capabilities |
|---|---|---|
| `SystemProfileView.tsx` | ✅ Implemented (34 KB) | Live hardware profile display, system overview. |
| `NewDiagnosisView.tsx` | ✅ Implemented (65 KB) | Full 8-step structured symptom intake workflow. |
| `DiagnosticIntakeReview.tsx` | ✅ Implemented (17 KB) | Pre-submission review of structured answers. |
| `DiagnosticResultView.tsx` | ✅ Implemented (28 KB) | Displays diagnosis output, confidence, evidence, action category. |
| `DiagnosticHistoryView.tsx` | ✅ Implemented (13 KB) | List of past diagnostic sessions. |
| `DiagnosticSessionDetailView.tsx` | ✅ Implemented (15 KB) | Full detail view of a single past session. |
| `RecurringPatternsView.tsx` | ✅ Implemented (20 KB) | Recurring symptom and cause detection across sessions. |
| `WarningSignsView.tsx` | ✅ Implemented (13 KB) | Aggregated warning signs display. |
| `HardwareDashboard.tsx` | ✅ Implemented (29 KB) | Live hardware monitoring dashboard. |
| `HelpScopeView.tsx` | ✅ Implemented (10 KB) | Scope / help documentation for end users. |
| `ReportsView.tsx` | ⚠️ Empty File | Not yet implemented. |

### 2.2 Components (`frontend/src/components/`)

| File | Status | Capabilities |
|---|---|---|
| `AppSidebar.tsx` | ✅ Implemented (8 KB) | Application-wide navigation sidebar. |
| `TopHeader.tsx` | ✅ Implemented (3 KB) | Top navigation bar. |
| `HardwareCard.tsx` | ⚠️ Empty File | Not yet implemented. |

### 2.3 Services (`frontend/src/services/`)

| File | Status | Capabilities |
|---|---|---|
| `profileService.ts` | ✅ Implemented (4 KB) | Axios-based API service for hardware profile operations. |

> **Note**: Other API calls (diagnosis, history, recurring) appear to be made inline in page components or through a shared API configuration rather than through a dedicated service layer for each module.

---

## 3. Summary: What Exists vs. What Is Missing

### Existing (Confirmed)
- ✅ Full structured 8-step symptom intake
- ✅ Rule-based diagnostic engine with scoring, evidence, and confidence
- ✅ AI explanation via Gemini (constrained)
- ✅ Hardware profile detection and persistence
- ✅ Diagnostic session persistence (with resolution fields)
- ✅ History, recurring pattern detection, and warning signs
- ✅ Resolution re-check workflow (the closed-loop seed)
- ✅ React frontend with all primary views (except Reports)

### Partial / Stubbed
- ⚠️ Remediation service exists but is early-stage
- ⚠️ `ReportsView.tsx` — empty file
- ⚠️ `HardwareCard.tsx` — empty file
- ⚠️ Frontend service layer only partially abstracted (`profileService.ts`; others inline)

### Not Yet Implemented
- ❌ Autonomous remediation planner / executor / verifier / rollback / pivot
- ❌ Dry-run simulation mode
- ❌ Remediation audit trail / `RemediationRun` data model
- ❌ `ActionAttempt`, `VerificationResult`, `RollbackEvent`, `PivotEvent` data models
- ❌ `SessionAnswer` as a first-class entity (currently stored as flat columns)
- ❌ `ReasoningFactor` / `OutputWarningSign` as relational entities
- ❌ PDF/print report export
- ❌ Supabase cloud sync worker
- ❌ Configurable offline/online mode
- ❌ WPF + WebView2 desktop packaging
