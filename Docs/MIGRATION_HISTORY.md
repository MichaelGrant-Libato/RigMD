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

## 15. Legacy Python Backend (Retired)

The legacy `backend/` FastAPI directory was **permanently retired and removed** in Phase 1 of the Agentic Re-Structuring (`4ff8199` on `exp-agentic-rebuild`).
All diagnostic, hardware observation, persistence, and autonomous ReAct remediation capabilities now run exclusively in `backend-dotnet/` (.NET 10).

Historical migration documentation remains preserved in:
- `Docs/Migration_Archive/BASELINE.md`
- `Docs/Migration_Archive/LEGACY_CAPABILITY_INVENTORY.md`
- `Docs/Migration_Archive/MIGRATION_MATRIX.md`

---

## 16. Current Development Status (`exp-agentic-rebuild`)

### Completed

- ✅ C# solution foundation (`RigMD.Domain`, `RigMD.Application`, `RigMD.Infrastructure`, `RigMD.Api`, `RigMD.Desktop`, `RigMD.Tests`)
- ✅ Legacy Python `backend/` and unused `RigMD.Agent` cloud polling queue purged (`-16,630` lines)
- ✅ Fake autonomy stubs (`RemediationPlanner`, `DryRunRemediationExecutor`, `RollbackManager`, `PivotEngine`) replaced with real tool-calling architecture
- ✅ 14-Tool Agent Tooling Layer (`IRigMdAgentTool`, `IRigMdAgentToolRegistry`) wrapping live WMI, LibreHardwareMonitor, Event Logs, and OS remediation actions
- ✅ Real non-destructive `PreviewImpactAsync` dry-run previews (counting actual files, bytes, running process locks, and Administrator elevation)
- ✅ Multi-turn ReAct Reasoning Engine (`AutonomousOrchestrator` + `GeminiReActLlmClient`) with `$0.00` local tool-calling fallback
- ✅ Paired `Tier0_ReadOnly` before/after telemetry verification (`PostExecutionVerificationReport`)
- ✅ Real-time SignalR streaming of `ReceiveReActStep` and `ReceiveProgress`
- ✅ React `AutonomyRemediationPanel` rewired with live `ReActTimeline`, raw JSON telemetry inspector, and interactive tool switcher
- ✅ Local-first SQLite persistence (`RigMdDbContext`) with optional startup PostgreSQL sync
- ✅ 45/45 automated unit and architecture tests passing (`dotnet test backend-dotnet/RigMD.slnx`)
- ✅ Frontend production build bundled into `RigMD.Api/wwwroot` (`npm.cmd run build`)

---

## 17. Important Git Checkpoints

**Legacy Python Reference**
```
python-backend-final
```

**C# Runtime Migration Complete**
```
csharp-migration-complete (983190e)
```

**4-Phase Agentic Re-Structuring (`exp-agentic-rebuild`)**
```
4ff8199 — Phase 1: Purge fake autonomy classes, cloud agent queue, hardcoded rule monoliths, and legacy python backend
e6607b1 — Phase 2: Implement Agent Tooling Layer wrapping real WMI providers and OS actions
3eb60bc — Phase 3: Implement multi-turn ReAct reasoning engine with tool calling, dry-run preview, and telemetry verification
8c253b8 — Phase 4: Rewire React frontend to stream live ReAct trace, dry-run impact preview, and telemetry verification
```

