# RigMD Migration History

This document preserves the historical migration state, architecture plans, and development status of the RigMD project as it transitioned from a passive Python advisory system to a C# closed-loop remediation platform.

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

- `Docs/Migration_Archive/BASELINE.md`
- `Docs/Migration_Archive/LEGACY_CAPABILITY_INVENTORY.md`

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
- ✅ improve Application/Infrastructure separation
- ⏳ replace anonymous/reflection-based internal models
- ⏳ improve database aggregation queries
- ✅ formalize remediation planner
- ✅ formalize safety policy
- ✅ add dry-run remediation
- ✅ formalize verification
- ✅ add rollback
- ✅ add pivot logic
- ✅ implement SQLite local persistence
- ✅ remediation history, audit, and failure deprioritization
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
