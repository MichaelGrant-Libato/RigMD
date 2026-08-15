# C# Migration & Autonomous Engine Roadmap

This document tracks RigMD's transition from the legacy Python/FastAPI backend to a C#/.NET Windows diagnostic and controlled remediation platform.

The roadmap is divided into two major goals:

1. **Runtime Migration** — move the active backend from Python/FastAPI to ASP.NET Core while preserving existing frontend functionality.
2. **V2 Architecture Evolution** — continue toward local persistence, stronger domain boundaries, controlled remediation, verification, rollback, pivoting, optional cloud synchronization, and desktop packaging.

The runtime migration has reached functional parity for the active React frontend.

The complete V2 architecture is still in progress.

---

## Current Migration Checkpoints

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

### Current C# backend

```
http://localhost:5273
```

### Frontend API configuration

```
VITE_API_BASE_URL=http://localhost:5273
```

---

## Architecture Direction

### Legacy Runtime

```
React
   ↓
Python / FastAPI
   ↓
Python Diagnostic Services
   ↓
Supabase PostgreSQL
```

### Current Runtime

```
React
   ↓
ASP.NET Core
   ↓
C# Application / Domain Services
   ↓
Npgsql
   ↓
Supabase PostgreSQL
```

### Target V2 Runtime

```
React / WPF + WebView2
          ↓
     ASP.NET Core
          ↓
   Application Layer
          ↓
      Domain Layer
          ↓
   Infrastructure
      ├── Windows APIs
      ├── SQLite
      ├── AI Explanation
      └── Optional Supabase Sync
```

---

## Roadmap Phases

### Phase 0 — Baseline & Architecture Alignment

**Status: COMPLETE**

Completed:

- Target architecture documented
- Migration architecture documented
- Autonomous engine architecture documented
- Remediation safety policy documented
- Legacy capabilities inventoried
- Migration matrix created
- Major architectural decisions recorded
- Core domain concepts identified
- SRS/SDD impact identified for future formal review

Relevant documents:

- `ARCHITECTURE.md`
- `ARCHITECTURE_MIGRATION.md`
- `AUTONOMOUS_ENGINE.md`
- `REMEDIATION_POLICY.md`
- `BASELINE.md`
- `LEGACY_CAPABILITY_INVENTORY.md`
- `MIGRATION_MATRIX.md`
- `DECISIONS.md`

---

### Phase 1 — Legacy Reference & Branching

**Status: COMPLETE**

Completed:

- Legacy Python state preserved in Git history
- Legacy Python reference tag available: `python-backend-final`
- Migration branch created: `feature/csharp-migration`
- C# migration checkpoint created: `csharp-migration-complete`

The old `backend/` directory remains temporarily in the repository for regression comparison and historical reference.

---

### Phase 2 — C#/.NET Foundation

**Status: COMPLETE**

Completed:

- Created `backend-dotnet/`
- Created `RigMD.sln`
- Created `RigMD.Api`
- Created `RigMD.Application`
- Created `RigMD.Domain`
- Created `RigMD.Infrastructure`
- Created `RigMD.Tests`
- Configured ASP.NET Core dependency injection
- Configured Windows-specific target frameworks where required
- Removed generated `bin/` and `obj/` files from Git tracking
- Added repository-wide .NET build output ignore rules

---

### Phase 3 — Domain Model & Core Structures

**Status: PARTIALLY COMPLETE**

Completed:

- Core diagnostic domain structures
- Diagnostic rule structures
- Action classification
- Confidence classification
- Profile models
- Recurring-pattern models
- Warning-sign models
- Resolution workflow support

Target architecture still requires:

- fully formalize `RemediationRun`
- fully formalize `ActionAttempt`
- fully formalize `VerificationResult`
- fully formalize `RollbackEvent`
- fully formalize `PivotEvent`
- complete repository boundaries between Application and Infrastructure
- replace remaining anonymous/reflection-based internal result handling with typed models

---

### Phase 4 — Windows Observation Layer

**Status: COMPLETE FOR CURRENT RUNTIME**

The Python hardware scanner has been replaced by C# Windows providers.

Current implementation supports:

- CPU
- GPU
- GPU driver information
- RAM
- storage devices
- storage type
- operating system
- motherboard information
- chipset/system metadata
- running process information
- live utilization data

Current endpoints:

```
GET  /api/hardware/live
POST /api/hardware/refresh
```

Additional observation providers may be added as the V2 architecture expands.

---

### Phase 5 — Persistence Architecture

**Status: MIGRATION COMPATIBILITY COMPLETE / TARGET ARCHITECTURE PENDING**

The C# runtime currently reads and writes the existing Supabase PostgreSQL schema through Npgsql.
This preserves existing diagnostic data while allowing Python-to-C# behavior to be compared.

Completed:

- C# database connectivity
- Diagnostic-session persistence
- Profile persistence
- Diagnostic history queries
- Dashboard queries
- Recurring-pattern queries
- Warning-sign queries
- Resolution-state persistence
- Database health endpoint

Current persistence:

```
ASP.NET Core
   ↓
DatabaseSessionService
   ↓
Npgsql
   ↓
Supabase PostgreSQL
```

Target persistence:

```
Application Repository Contracts
   ↓
Entity Framework Core
   ↓
SQLite Local Source of Truth
   ↓
Optional Async Supabase Synchronization
```

Remaining:

- introduce repository contracts at the Application boundary
- implement EF Core SQLite persistence
- migrate local persistence to SQLite
- implement optional Supabase synchronization
- implement synchronization conflict handling
- replace direct database access patterns where appropriate

---

### Phase 6 — ASP.NET Core API

**Status: COMPLETE FOR ACTIVE FRONTEND PARITY**

Current implemented endpoints include:

```
GET  /api/hardware/live
POST /api/hardware/refresh

GET  /api/dashboard/summary

POST /api/diagnosis/submit
GET  /api/diagnosis/sessions
GET  /api/diagnosis/sessions/{sessionId}

POST /api/diagnosis/{sessionId}/needs-recheck
POST /api/diagnosis/{sessionId}/check-resolution

POST /api/profiles/save

GET  /api/recurring/patterns
GET  /api/recurring/patterns/{patternId}

GET  /api/warning-signs/reference

GET  /api/remediation/actions
POST /api/remediation/execute
POST /api/remediation/open-target

GET  /api/database/health
```

Unused legacy Python routes were not recreated if no active frontend feature or requirement depended on them.

---

### Phase 7 — Connect the React Frontend

**Status: COMPLETE**

The active React frontend has been migrated from the Python backend to the C# backend.

Current environment:

```
VITE_API_BASE_URL=http://localhost:5273
```

Completed:

- Removed active frontend fallback to port 8000
- Connected hardware dashboard
- Connected system profile
- Connected diagnostic submission
- Connected diagnostic history
- Connected session details
- Connected recurring patterns
- Connected warning signs
- Connected remediation actions
- Connected profile save
- Connected resolution rechecking
- Frontend production build passes

---

### Phase 8 — Diagnostic Engine Migration

**Status: COMPLETE FOR CURRENT PARITY**

Completed:

- Diagnostic logic migrated to C#
- Deterministic scoring retained
- Evidence logic migrated
- Category classification migrated
- Action-category behavior migrated
- Confidence behavior migrated
- Frontend diagnosis submission now uses ASP.NET Core
- Diagnostic results persist successfully to the existing database schema

Further refactoring may occur as the Domain and Application boundaries are strengthened.

---

### Phase 9 — Controlled Remediation Framework

**Status: COMPLETE**

Current implementation already includes:

- Remediation action discovery
- Remediation execution endpoint
- Controlled target opening
- Session `needs_recheck` state
- Resolution checking

Current endpoints:

```
GET  /api/remediation/actions
POST /api/remediation/execute
POST /api/remediation/open-target
```

Target framework still requires:

- `IRemediationPlanner`
- formal `ISafetyPolicy`
- formal `IRemediationRegistry`
- formal `IRemediationExecutor`
- formal `IVerificationService`
- `IRollbackManager`
- `IPivotEngine`

Only explicitly registered and approved remediation actions may execute.
AI must never generate arbitrary Windows commands for automatic execution.

---

### Phase 10 — Dry-Run Autonomous Engine

**Status: COMPLETE**

Target flow:

```
DIAGNOSE
   ↓
PLAN
   ↓
SAFETY CHECK
   ↓
DRY RUN
   ↓
USER APPROVAL WHEN REQUIRED
```

Dry-run support should remain available as a permanent developer, testing, and validation feature.

---

### Phase 11 — First Fully Controlled Autonomous Action

**Status: PENDING**

Implement and formally validate one narrowly bounded remediation action with:

- known preconditions
- known execution behavior
- measurable postconditions
- rollback or safe recovery
- audit logging
- deterministic verification

Only after this pattern is proven should the supported remediation catalog expand.

---

### Phase 12 — Verification, Rollback & Pivot

**Status: PARTIALLY STARTED**

Current resolution checking already provides an early verification mechanism.

Current flow:

```
Action
   ↓
Mark Needs Recheck
   ↓
Collect Live State
   ↓
Resolution Check
```

Target flow:

```
ACT
 ↓
VERIFY
 ↓
Resolved?
 ├── Yes → COMPLETE
 └── No
      ↓
   ROLLBACK
      ↓
     PIVOT
      ↓
NEXT SAFE ACTION
```

Remaining:

- formal verification result objects
- rollback manager
- pivot engine
- remediation-attempt history
- verification audit data

---

### Phase 13 — History, Audit & Recurring Patterns

**Status: PARTIALLY COMPLETE**

Already implemented:

- Diagnostic history
- Session details
- Recurring issue calculation
- Recurring pattern details
- Warning-sign observation history
- Resolution status fields

Remaining:

- formal remediation-attempt audit trail
- verification history
- rollback history
- pivot history
- remediation outcome analytics

---

### Phase 14 — AI Explanation Integration

**Status: EXISTING CAPABILITY / ARCHITECTURAL HARDENING PENDING**

AI may be used for:

- plain-language explanation
- summarization
- presentation

AI must not decide or generate arbitrary Windows commands for automatic execution.

The deterministic C# system remains authoritative for:

- diagnosis
- confidence
- supported action selection
- safety policy
- execution
- verification
- rollback behavior
- pivot behavior

---

### Phase 15 — Optional Cloud Sync

**Status: PENDING**

Target architecture:

```
SQLite
   ↓
Optional Async Synchronization
   ↓
Supabase
```

The current direct Supabase persistence layer is a migration-compatibility implementation, not the final offline-first design.

Remaining:

- synchronization worker
- queue/retry behavior
- synchronization metadata
- conflict resolution
- offline recovery behavior

---

### Phase 16 — Desktop Packaging

**Status: PENDING**

Target desktop structure:

```
WPF
 +
WebView2
 +
React UI
 +
C# Backend
```

Goals:

- standalone Windows application
- no Python runtime requirement
- local diagnostic functionality
- optional network-dependent features
- controlled application startup and shutdown

---

### Phase 17 — Performance, Testing & Thesis Validation

**Status: IN PROGRESS**

Current validation:

- C# solution builds successfully
- `dotnet test` executes successfully
- frontend production build passes
- frontend communicates with the C# backend
- active API route parity reviewed
- database connectivity verified
- recurring patterns verified against current data
- warning-sign results verified against current data
- resolution flow tested

Automated test coverage is still limited.

Priority tests:

- diagnostic rule classification tests
- confidence calculation tests
- recurring-pattern tests
- warning-sign normalization tests
- resolution-state tests
- remediation safety tests
- persistence integration tests
- API contract tests

Target SRS and thesis validation should eventually include:

- startup performance
- rule-based diagnostic response time
- AI explanation response time
- hardware-detection reliability
- diagnostic accuracy
- remediation safety
- verification correctness
- rollback correctness
- recurring-pattern accuracy

---

## Current Development Priority

The Python-to-C# runtime migration has reached functional parity for the active React frontend.

Immediate priorities are:

- expand meaningful automated test coverage
- clean Application and Infrastructure boundaries
- replace anonymous/reflection-based internal handling with typed models
- improve database aggregation queries
- continue the controlled remediation framework
- implement dry-run behavior
- formalize verification, rollback, and pivot
- implement SQLite local persistence
- implement optional Supabase synchronization
- perform final C# validation
- retire the legacy Python runtime
- prepare desktop packaging
- align final implementation with the SRS and SDD