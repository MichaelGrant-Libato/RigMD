# Architecture Migration Plan

## 1. Purpose

RigMD is transitioning from a legacy Python/FastAPI diagnostic backend into a C#/.NET Windows diagnostic and controlled remediation platform.

The migration is intentionally incremental and separates two concerns:

1. **Runtime Migration** — replace the active Python/FastAPI backend with ASP.NET Core while preserving existing functionality.
2. **Architecture Evolution** — move toward stronger domain boundaries, offline-first persistence, controlled remediation, verification, rollback, pivoting, optional cloud synchronization, and Windows desktop packaging.

The runtime migration has reached functional parity for the active React frontend.

The complete V2 architecture is still in progress.

---

## 2. Current Migration Checkpoints

### Legacy Python Reference

```
Tag: python-backend-final
```

### C# Runtime Migration Complete

```
Commit: 983190e
Tag: csharp-migration-complete
Branch: feature/csharp-migration
```

### Current frontend

```
http://localhost:5173
```

### Current backend

```
http://localhost:5273
```

### Frontend API configuration

```
VITE_API_BASE_URL=http://localhost:5273
```

---

## 3. Architecture Alignment Matrix

| Aspect | Legacy Baseline | Current Runtime | Target V2 State |
|---|---|---|---|
| Backend | Python / FastAPI | C# / ASP.NET Core | C# / ASP.NET Core |
| Frontend | React / Vite | React / Vite | React + WPF/WebView2 |
| Hardware Telemetry | Python wrappers / WMI | C# Windows providers / WMI | C# native Windows providers |
| Diagnostic Engine | Python | C# | C# Domain layer |
| Persistence | Supabase PostgreSQL | Supabase PostgreSQL through Npgsql | SQLite local source of truth |
| Cloud Dependency | Required for persistence | Required for current persistence | Optional Supabase synchronization |
| System Behavior | Advisory / manual action | Controlled autonomous remediation (single action) | Closed-loop remediation |
| Verification | Basic resolution checking | Formal verification (file count + size delta) | Formal verification |
| Rollback | Not formalized | Not formalized | Planned |
| Pivot | Not formalized | Not formalized | Planned |
| AI Role | Explanation | Explanation | Explanation only |
| Packaging | Development runtime | Development runtime | Standalone Windows desktop application |

---

## 4. Legacy Architecture

```
React Frontend
      ↓
Python / FastAPI
      ↓
Python Diagnostic Services
      ↓
Python Hardware Detection
      ↓
Supabase PostgreSQL
```

The legacy Python implementation included:

- hardware detection
- diagnostic scoring
- confidence calculation
- history
- recurring patterns
- warning signs
- profile persistence
- remediation helpers
- resolution checking
- AI explanation

The legacy runtime remains available under:

```
backend/
```

It is retained temporarily for regression comparison and historical reference.
It is no longer the active backend used by the current React frontend.

---

## 5. Current Runtime Architecture

```
React / Vite
      ↓
ASP.NET Core API
      ↓
Application / Domain Services
      ↓
Infrastructure
   ├── Windows/WMI Providers
   └── DatabaseSessionService
              ↓
            Npgsql
              ↓
     Supabase PostgreSQL
```

This architecture represents the current functional migration state.

The C# backend currently performs:

- Windows hardware observation
- diagnostic processing
- session persistence
- profile persistence
- diagnostic history
- dashboard aggregation
- recurring-pattern detection
- warning-sign normalization
- remediation execution
- resolution checking

---

## 6. Current API Migration Status

### Hardware

```
GET  /api/hardware/live
POST /api/hardware/refresh
```

### Dashboard

```
GET /api/dashboard/summary
```

### Diagnosis

```
POST /api/diagnosis/submit
```

### History

```
GET /api/diagnosis/sessions
GET /api/diagnosis/sessions/{sessionId}
```

### Resolution

```
POST /api/diagnosis/{sessionId}/needs-recheck
POST /api/diagnosis/{sessionId}/check-resolution
```

### Profiles

```
POST /api/profiles/save
```

### Recurring Patterns

```
GET /api/recurring/patterns
GET /api/recurring/patterns/{patternId}
```

### Warning Signs

```
GET /api/warning-signs/reference
```

### Remediation

```
GET  /api/remediation/actions
POST /api/remediation/execute
POST /api/remediation/open-target
```

### Database Health

```
GET /api/database/health
```

Unused legacy routes are not required to be recreated unless they are needed by an active frontend feature, SRS/SDD requirement, or future architecture.

---

## 7. Target V2 Architecture

```
                  React UI
                     ↓
            WPF + WebView2 Shell
                     ↓
               ASP.NET Core
                     ↓
              Application Layer
                     ↓
                 Domain
                     ↓
              Infrastructure
          ┌──────────┼──────────┐
          ↓          ↓          ↓
     Windows APIs   SQLite     AI Client
                     ↓
              Optional Sync
                     ↓
                 Supabase
```

The final architecture separates system responsibilities clearly.

---

## 8. Domain Layer

The Domain layer is responsible for deterministic business behavior.

Responsibilities include:

- diagnostic rules
- scoring
- category classification
- confidence calculation
- evidence generation
- warning-sign rules
- remediation policies
- safety classifications
- verification rules
- rollback rules
- pivot rules

The Domain layer should not depend directly on:

- HTTP
- Supabase
- Npgsql
- SQLite
- WMI
- external APIs
- UI code

---

## 9. Application Layer

The Application layer is responsible for orchestration.

Responsibilities include:

- diagnostic use cases
- profile use cases
- session-history use cases
- recurring-pattern use cases
- remediation planning
- verification orchestration
- repository contracts
- provider contracts
- coordination between Domain and Infrastructure

Target repository contracts include concepts such as:

- `IProfileRepository`
- `IDiagnosticSessionRepository`
- `IRemediationRepository`

The current implementation still contains some direct Infrastructure coupling that should be cleaned up in later refactoring.

---

## 10. Infrastructure Layer

The Infrastructure layer is responsible for implementation details external to the Domain.

Responsibilities include:

- WMI and Windows system access
- hardware providers
- process information
- storage information
- database access
- Npgsql integration
- future SQLite persistence
- AI clients
- future Supabase synchronization
- concrete remediation actions

Current database compatibility is handled through the Infrastructure layer using Npgsql and Supabase PostgreSQL.

---

## 11. API Layer

The ASP.NET Core API is responsible for presentation and HTTP concerns.

Responsibilities include:

- routing
- request validation
- response formatting
- dependency injection
- exception handling
- CORS configuration
- exposing Application services to the frontend

The API should avoid owning diagnostic business rules directly.

---

## 12. Persistence Migration

### Current State

The C# backend currently accesses the existing Supabase PostgreSQL schema through Npgsql.

This is deliberate. It allows the migration to preserve:

- existing profiles
- diagnostic sessions
- diagnostic history
- recurring-pattern history
- warning-sign observations
- resolution state

Current flow:

```
ASP.NET Core
     ↓
Infrastructure
     ↓
DatabaseSessionService
     ↓
Npgsql
     ↓
Supabase PostgreSQL
```

### Target State

The final offline-first persistence architecture is:

```
Application
    ↓
Repository Contracts
    ↓
Entity Framework Core
    ↓
SQLite
    ↓
Optional Background Synchronization
    ↓
Supabase
```

SQLite will become the local source of truth.
Supabase will become optional rather than required for basic diagnostic functionality.

---

## 13. Controlled Remediation Migration

Current remediation functionality already provides:

- supported action discovery
- supported action execution
- controlled target opening
- session `needs_recheck` state
- resolution checking

This is the foundation of the future closed-loop remediation engine.

Target flow:

```
Diagnosis
   ↓
Remediation Planner
   ↓
Safety Policy
   ↓
Dry Run
   ↓
Approval if Required
   ↓
Registered Action Executor
   ↓
Verification
   ↓
Resolved?
 ├── Yes → Complete
 └── No
       ↓
    Rollback
       ↓
      Pivot
       ↓
 Next Safe Candidate
```

Only officially registered and reviewed actions should be executable.
The architecture must never permit an LLM to generate arbitrary system commands for automatic execution.

---

## 14. AI Responsibility

AI is intentionally constrained.

AI may be used for:

- explanation
- summarization
- presentation of deterministic results

AI must not be responsible for:

- diagnostic rule decisions
- action authorization
- arbitrary command generation
- safety policy
- automatic rollback decisions
- uncontrolled system modification

The deterministic C# engine remains authoritative.

---

## 15. Migration Execution Status

### Runtime Migration — Completed for Active Frontend

Completed:

- C# solution foundation
- ASP.NET Core API
- C# diagnostic engine
- Windows observation layer
- Supabase compatibility through Npgsql
- frontend migration to port 5273
- profile persistence
- diagnostic history
- dashboard
- recurring patterns
- warning signs
- remediation endpoints
- resolution checking

### Architecture Evolution — Still In Progress

Remaining:

- repository interfaces
- stronger Application/Infrastructure separation
- replacement of reflection/anonymous internal mappings
- improved database aggregation
- SQLite persistence
- optional Supabase sync
- remediation planner
- formal safety policy
- remediation registry
- dry-run mode
- verification objects
- rollback manager
- pivot engine
- remediation audit trail
- desktop packaging

---

## 16. Testing and Validation

Current validation includes:

- successful .NET build
- successful `dotnet test`
- successful frontend production build
- active frontend API route parity
- database connectivity
- recurring-pattern validation
- warning-sign validation
- resolution-flow validation

Automated test coverage is still limited and should be expanded before the Python runtime is retired.

Priority areas:

- diagnostic-rule tests
- confidence tests
- recurring-pattern tests
- warning-sign normalization tests
- resolution tests
- remediation safety tests
- persistence integration tests

---

## 17. Python Runtime Retirement

The old Python backend should remain available until final validation is complete.

Before deleting:

```
backend/
```

the project should complete:

- meaningful automated C# tests
- backend build and test validation
- frontend production build
- full UI smoke testing
- verification that no active frontend call uses port 8000
- verification that no startup workflow requires FastAPI or Uvicorn
- active documentation cleanup
- final regression comparison

After those conditions are satisfied, the Python runtime can be removed in a dedicated retirement commit.

Historical documentation should remain available even after runtime retirement.

Relevant historical files include:

- `Docs/BASELINE.md`
- `Docs/LEGACY_CAPABILITY_INVENTORY.md`

---

## 18. Academic Documentation

Official thesis documents such as the SRS and SDD should not be silently modified during implementation.

Architecture changes that materially affect the approved system scope should be formally reviewed and incorporated only after they have been:

- implemented
- validated
- accepted by the development team
- reviewed with the appropriate academic stakeholders

The implementation repository may document target architecture earlier, but the academic documents remain the formal source of approved system requirements until updated through the proper process.

---

## 19. Current Architecture Priority

The Python-to-C# runtime migration is functionally complete for the active React frontend.

The next architectural priorities are:

- expand automated test coverage
- clean Application and Infrastructure boundaries
- formalize controlled remediation abstractions
- implement dry-run behavior
- formalize verification, rollback, and pivot
- implement SQLite local persistence
- implement optional Supabase synchronization
- complete final migration validation
- retire the legacy Python runtime
- prepare WPF + WebView2 desktop packaging