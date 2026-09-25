# Architectural Decision Records

This document records major architecture decisions made during the RigMD V2 migration.

---

## DECISION-001: Migrate the Active Backend to C# / ASP.NET Core

- **Date:** August 2026
- **Status:** Implemented

### Context

The legacy RigMD backend used Python/FastAPI together with Windows-oriented Python libraries and WMI integrations.

The project requires deeper Windows integration, stronger type safety, easier Windows packaging, and a cleaner foundation for controlled remediation.

### Decision

The active backend will use C# / .NET with ASP.NET Core.

### Reason

C# provides direct integration with Windows technologies including:

- `System.Management`
- WMI
- CIM-compatible APIs
- Windows process APIs
- Windows Event Logs
- native desktop packaging options

It also allows the diagnostic, remediation, and desktop application layers to share a common .NET ecosystem.

### Current Result

The React frontend now communicates with the ASP.NET Core backend at:

```
http://localhost:5273
```

The active frontend no longer depends on the Python/FastAPI backend.

Migration checkpoint:

```
Commit: 983190e
Tag: csharp-migration-complete
```

Legacy Python reference:

```
Tag: python-backend-final
```

### Consequences

Positive:

- better Windows-native integration
- stronger compile-time validation
- improved maintainability
- easier future Windows packaging
- shared C# ecosystem for diagnostics and remediation

Tradeoffs:

- substantial migration effort
- legacy behavior must be regression-tested
- architecture cleanup remains necessary after functional parity

The `backend/` Python directory remains temporarily for regression comparison and historical reference.

---

## DECISION-002: Preserve the React Frontend During Backend Migration

- **Date:** August 2026
- **Status:** Implemented

### Context

The existing React frontend already contained substantial completed functionality.
Rewriting both the frontend and backend at the same time would increase migration risk and make regression analysis more difficult.

### Decision

Preserve the existing React/Vite frontend and redirect it to the new ASP.NET Core backend.

### Current Result

Frontend:

```
http://localhost:5173
```

Backend:

```
http://localhost:5273
```

Frontend configuration:

```
VITE_API_BASE_URL=http://localhost:5273
```

### Consequences

The migration could focus on backend behavior while preserving existing screens and workflows.

Active frontend API calls should use the configured API base URL rather than depending on the legacy FastAPI server or an implicit Vite proxy.

---

## DECISION-003: Preserve Existing Supabase Persistence During Runtime Migration

- **Date:** August 2026
- **Status:** Implemented as migration compatibility architecture

### Context

The target architecture calls for SQLite as the local source of truth.

However, migrating the backend language and database architecture at the same time would make regression analysis significantly more difficult.

The existing Supabase PostgreSQL database also contains useful diagnostic history that should remain available during migration.

### Decision

During C# migration parity, the ASP.NET Core backend will continue using the existing Supabase PostgreSQL schema through Npgsql.

### Reason

This allows:

- existing data to remain intact
- Python and C# behavior to be compared
- frontend migration to be validated independently
- database redesign to occur as a separate controlled phase
- diagnostic history to remain available

### Current Architecture

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

### Consequences

The current direct Supabase dependency is not the final offline-first architecture.

The current implementation also contains database responsibilities that should later move behind Application-level repository contracts.

---

## DECISION-004: SQLite Will Become the Local Source of Truth

- **Date:** August 2026
- **Status:** Planned

### Context

Diagnostics may need to run while the computer has limited or unavailable network connectivity.

A mandatory cloud database introduces an unnecessary failure point for a desktop diagnostic application.

### Decision

The final persistence architecture will use SQLite locally, with Entity Framework Core providing persistence support.

### Target Architecture

```
Application
    ↓
Repository Interfaces
    ↓
Entity Framework Core
    ↓
SQLite
```

### Reason

SQLite enables:

- offline diagnostic operation
- local history
- lower network dependency
- improved reliability
- faster access to recent diagnostic state

### Consequences

Required future work includes:

- SQLite schema design
- EF Core mappings
- migrations
- repository implementations
- migration of current persistence logic
- local database lifecycle management

---

## DECISION-005: Supabase Will Become Optional Cloud Synchronization

- **Date:** August 2026
- **Status:** Planned

### Context

Supabase remains useful for backup, synchronization, reporting, and future multi-device scenarios.

It should not be required for core diagnostic functionality.

### Decision

Supabase will eventually operate as an optional asynchronous synchronization destination rather than the primary source of truth.

### Target Architecture

```
SQLite
   ↓
Background Synchronization
   ↓
Supabase
```

### Consequences

The project will require:

- synchronization metadata
- retry behavior
- offline queues
- conflict resolution
- synchronization status tracking
- recovery from failed synchronization attempts

---

## DECISION-006: Diagnostic Decisions Remain Deterministic

- **Date:** August 2026
- **Status:** Superseded by DECISION-019 & DECISION-020 (`exp-agentic-rebuild`)

### Context

RigMD provides diagnostic guidance and may perform remediation actions.

Allowing an LLM to independently determine diagnostic rules or system actions would make system behavior difficult to verify and unsafe to execute.

### Decision

The following behavior must remain deterministic and implemented in C#:

- diagnostic classification
- scoring
- confidence calculation
- warning-sign evaluation
- supported action selection
- remediation safety logic
- verification rules
- rollback rules
- pivot rules

### Consequences

AI may assist with explanation and summarization but does not replace the deterministic diagnostic and remediation engine.

---

## DECISION-007: AI Is Restricted to Explanation and Presentation

- **Date:** August 2026
- **Status:** Superseded by DECISION-019 & DECISION-020 (`exp-agentic-rebuild`)

### Decision

AI may be used for:

- plain-language explanation
- summarization
- contextual presentation of deterministic results

AI must not:

- generate arbitrary Windows commands for automatic execution
- choose unregistered remediation actions
- bypass remediation safety policy
- independently modify the operating system
- authorize high-risk actions
- control rollback behavior outside deterministic policy

### Consequences

The deterministic C# system remains authoritative.

AI output should be treated as explanatory content rather than executable system policy.

---

## DECISION-008: Controlled Autonomous Remediation

- **Date:** August 2026
- **Status:** In Progress

### Context

The original system primarily provided advisory results.

The V2 direction adds the ability to perform carefully bounded remediation actions.

### Decision

RigMD may evolve into a controlled closed-loop remediation system using the following general pipeline:

```
Diagnose
   ↓
Plan
   ↓
Safety Check
   ↓
Dry Run
   ↓
User Approval When Required
   ↓
Execute Registered Action
   ↓
Verify
   ↓
Resolved?
   ├── Yes → Complete
   └── No
        ↓
      Rollback
        ↓
       Pivot
        ↓
   Next Safe Action
```

### Safety Requirement

Only registered and reviewed remediation actions may execute.

The system must never expose unrestricted shell, PowerShell, registry, or command execution as an autonomous capability.

### Consequences

The project requires:

- remediation registry
- remediation planner
- safety tiers
- verification
- rollback
- pivot logic
- audit history
- meaningful automated tests

---

## DECISION-009: Existing Resolution Checking Is the Seed of the Closed Loop

- **Date:** August 2026
- **Status:** Implemented

### Context

The current C# backend already supports marking a diagnostic session for rechecking and evaluating whether an issue remains active.

Current endpoints:

```
POST /api/diagnosis/{sessionId}/needs-recheck
POST /api/diagnosis/{sessionId}/check-resolution
```

### Decision

The existing resolution workflow will be retained and evolved into the formal verification stage of the future autonomous remediation architecture.

### Consequences

Future verification models should build on the existing resolution workflow rather than replacing it unnecessarily.

The current implementation represents an early version of:

```
Action
   ↓
Needs Recheck
   ↓
Collect Current State
   ↓
Evaluate Resolution
```

---

## DECISION-010: Do Not Recreate Unused Legacy Endpoints Without a Requirement

- **Date:** August 2026
- **Status:** Active

### Context

The legacy Python backend contains routes that are not used by the current React frontend.

Examples include some profile CRUD and hardware-test routes.

### Decision

Legacy routes should only be migrated when required by:

- an active frontend feature
- an SRS/SDD requirement
- a future architectural requirement
- a confirmed integration requirement

### Reason

Blindly recreating every historical endpoint would preserve unnecessary legacy design and increase maintenance cost.

### Consequences

Migration parity is evaluated against required functionality rather than route count alone.

---

## DECISION-011: Generated Build Output Must Not Be Version Controlled

- **Date:** August 2026
- **Status:** Implemented

### Context

The repository previously tracked .NET-generated build files.

This caused Git changes to include DLLs, PDB files, NuGet caches, generated project files, and test binaries.

### Decision

Repository-wide .NET build output is ignored:

```
**/bin/
**/obj/
```

Frontend production output is also ignored:

```
frontend/dist/
```

### Current Result

Tracked generated build artifacts were removed from Git as part of the C# migration checkpoint.

### Consequences

Future commits should contain source code and configuration rather than generated build output.

---

## DECISION-012: Legacy Python Runtime Is Retained Until Final Validation

- **Date:** August 2026
- **Status:** Retired in Phase 1 of `exp-agentic-rebuild` (`4ff8199`)

### Context

Functional C# migration parity has been reached for the active frontend.

However, automated test coverage is still limited.

### Decision

Do not immediately delete:

```
backend/
```

The directory remains temporarily for:

- regression comparison
- historical reference
- migration verification
- behavior auditing

### Retirement Conditions

The Python runtime should only be removed after:

- meaningful automated C# tests are added
- backend build and test validation passes
- frontend production build passes
- full UI smoke testing passes
- no active frontend call uses port 8000
- no startup workflow requires FastAPI or Uvicorn
- active documentation has been updated

### Consequences

Once those conditions are satisfied, the Python runtime can be removed in a dedicated retirement commit.

Historical documentation such as:

- `Docs/Migration_Archive/BASELINE.md`
- `Docs/Migration_Archive/LEGACY_CAPABILITY_INVENTORY.md`

should remain in the repository.

---

## DECISION-013: Runtime Migration and V2 Architecture Evolution Are Separate Milestones

- **Date:** August 2026
- **Status:** Active

### Context

The Python-to-C# backend migration and the complete V2 architecture are related but separate efforts.

The current C# runtime has achieved functional frontend parity while some target architectural components remain unfinished.

### Decision

The project will explicitly distinguish:

**Runtime Migration** from **V2 Architecture Evolution**.

Runtime migration includes:

- replacing FastAPI with ASP.NET Core
- migrating diagnostic behavior
- migrating frontend API calls
- preserving current persistence behavior
- reaching functional parity

V2 architecture evolution includes:

- SQLite local persistence
- repository abstractions
- optional Supabase synchronization
- dry-run remediation
- formal verification
- rollback
- pivoting
- desktop packaging

### Consequences

The project must not claim that the entire V2 architecture is complete merely because frontend-to-C# runtime parity has been achieved.

---

## DECISION-014: Academic Requirements Must Not Be Silently Rewritten

- **Date:** August 2026
- **Status:** Active

### Context

The implementation is evolving beyond some aspects of the originally documented advisory architecture.

The SRS and SDD remain formal academic documents.

### Decision

Implementation documentation may describe proposed and target architecture, but formal SRS/SDD requirements should only be changed through the appropriate academic review process.

### Consequences

Architecture work that expands or materially changes project scope should be:

- implemented
- validated
- documented
- reviewed by the team
- discussed with the appropriate academic stakeholders

The repository should distinguish between:

- current implementation
- target architecture
- formally approved academic scope

---

## DECISION-015: Remediation Pivot Engine for Failed Actions

- **Date:** August 2026
- **Status:** Superseded by DECISION-020 (`exp-agentic-rebuild`)

### Context

During autonomous remediation, the system executes a planned action and verifies the outcome. If an action fails or rolls back, the system previously lacked a mechanism to attempt alternative solutions.

### Decision

A Pivot Engine (`IPivotEngine`) has been introduced to the `AutonomousOrchestrator` execution loop. When an action fails or is rolled back, the Pivot Engine removes the failed action from the `RemediationPlan`, updates the `StrategyReasoning` to reflect the pivot, and the orchestrator loop attempts the next available action.

### Consequences

The orchestrator now evaluates a `List<RemediationAttempt>` rather than a single attempt per cycle, tracking the history of all actions tried in a single plan until either resolution is achieved or all safe actions are exhausted.

---

## DECISION-016: SignalR for Live Remediation Progress Streaming

- **Date:** September 2026
- **Status:** Implemented & Expanded in Phase 3/4 (`ReceiveReActStep`)

### Context

When the autonomous engine executes remediation actions (e.g., clearing thousands of temporary files or running Windows Disk Cleanup), the operation can take several seconds to minutes. The user had no visibility into what the system was doing until the HTTP response returned with the final result.

### Decision

A SignalR Hub (`RemediationHub`) was introduced to stream real-time progress messages and structured `ReActTraceStep` events from diagnostic and remediation tools to the frontend during both investigation and execution.

### Implementation

```
AutonomyController (progressReporter + stepReporter lambdas)
    → AutonomousOrchestrator (ReAct Loop)
    → IRigMdAgentTool / WindowsRemediationExecutor
    → IHubContext<RemediationHub>.Clients.All.SendAsync("ReceiveReActStep" / "ReceiveProgress", ...)
    → WebSocket → React frontend (AutonomyRemediationPanel)
```

### CORS Requirement

SignalR WebSocket connections require `.AllowCredentials()` in the CORS policy when the frontend runs on a different origin (port 5173 → 5273).

### Consequences

- Users can observe every `Thought -> Tool Call -> Observation -> Dry-Run Preview -> Execution -> Verification` step in real time.
- Both `progressReporter` and `stepReporter` callbacks are optional so all existing code paths remain unaffected.

---

## DECISION-017: Device and Battery Awareness (WMI)

- **Date:** September 2026
- **Status:** Implemented

### Context

The original diagnostic engine assumed a generic desktop form factor, making incorrect assumptions about thermal throttling and hardware limits when diagnosing laptops (e.g., advising users to check case fans instead of power plans or cooling pads).

### Decision

WMI queries were added to directly detect the chassis type (`Win32_SystemEnclosure`) and battery health/status (`Win32_Battery`).

### Implementation

- `WmiDeviceTypeProvider` maps `ChassisTypes` (e.g., 3=Desktop, 9=Laptop, 10=Notebook, 30=Tablet).
- `WmiBatteryProvider` collects charge percentage, estimated run time, and real-time charging status.
- Exposed as a dedicated `Tier0_ReadOnly` agent tool (`InspectBatteryAndPowerTool`) and included in `HardwareProfileDto`.

---

## DECISION-018: Phase 1 Purge — Retire Fake Autonomy Stubs, Cloud Agent Queue, and Legacy Python (`4ff8199`)

- **Date:** September 2026
- **Status:** Implemented (`exp-agentic-rebuild`)

### Context

A technical audit revealed that the initial autonomy classes (`RemediationPlanner`, `RemediationRegistry`, `SafetyPolicy`, `PivotEngine`, `DryRunRemediationExecutor`, `RollbackManager`, `VerificationService`), the 3,300+ lines of hardcoded scoring rules in `DiagnosticEngine.cs` / `AutomaticDiagnosisService.cs`, and the 390-line frontend `ACTION_COPY` dictionary were static facades or no-op stubs rather than genuine autonomous reasoning. In addition, the legacy `backend/` Python folder and the unused `RigMD.Agent` cloud PostgreSQL polling queue added dead weight.

### Decision

Execute a clean architectural purge (`-16,630 lines` across 89 files):
1. Permanently delete the legacy `backend/` Python FastAPI directory.
2. Delete the cloud-polling `RigMD.Agent` Windows Service project, `AgentController`, and `AgentRepository`.
3. Delete all 14 fake autonomy classes/interfaces (`RemediationPlanner`, `RemediationRegistry`, `SafetyPolicy`, `PivotEngine`, `DryRunRemediationExecutor`, `RollbackManager`, `VerificationService`) while retaining the real Windows WMI hardware providers and real OS remediation action primitives.

### Consequences

- Eliminated simulated dry-runs (`Thread.Sleep` + hardcoded strings) and fake rollback claims.
- Established a clean, local-first `.NET 10` foundation for real tool-calling autonomy.

---

## DECISION-019: Phase 2 Tooling Layer — Expose Real WMI Providers and OS Actions as Typed Agent Tools (`e6607b1`)

- **Date:** September 2026
- **Status:** Implemented (`exp-agentic-rebuild`)

### Context

For an LLM or ReAct agent to genuinely diagnose and remediate Windows issues without hallucinating or executing unsafe shell commands, it needs strongly-typed, bounded tools backed by real Windows telemetry and OS primitives.

### Decision

Introduce `IRigMdAgentTool` and `IRigMdAgentToolRegistry` with 14 registered tools classified across three `ToolSafetyTier` levels:
- **7 `Tier0_ReadOnly` Diagnostic Tools**: `inspect_cpu_and_thermals`, `inspect_memory_and_processes`, `inspect_storage_health`, `inspect_gpu_and_displays`, `inspect_network_connectivity`, `inspect_battery_and_power`, `query_windows_event_logs`.
- **3 `Tier1_SafeReversible` Maintenance Tools**: `clear_temp_files`, `flush_dns_cache`, `restart_windows_explorer`.
- **4 `Tier2_DestructiveOrAdmin` Remediation Tools**: `terminate_processes` (with hard protected-process denylist), `clear_browser_cache`, `clear_windows_update_cache`, `run_system_file_checker` (with UTF-16LE output decoding and Administrator elevation checks).

Every tool exposes a real, non-destructive `PreviewImpactAsync` method that measures actual files, bytes, running process locks, and elevation requirements before execution.

---

## DECISION-020: Phase 3 Multi-Turn ReAct Reasoning Engine & Paired Telemetry Verification (`3eb60bc`)

- **Date:** September 2026
- **Status:** Implemented (`exp-agentic-rebuild`)

### Context

The autonomous orchestrator must perform genuine multi-step investigation (`Thought -> Tool Call -> Observation`) before proposing a remediation, and must objectively verify system state before and after execution—all while strictly maintaining a `$0.00` operational budget.

### Decision

1. Implement `GeminiReActLlmClient` (`IReActLlmClient`) supporting both **Google Gemini Free-Tier Function Calling** (`gemini-2.5-flash` / `gemini-2.0-flash`) and a **$0.00 Built-In Local Tool-Calling ReAct Engine** when offline or when no API key is configured.
2. Upgrade `AutonomousOrchestrator` to run up to 4 turns of `Tier0_ReadOnly` tool calls, intercept any direct `Tier1`/`Tier2` tool calls behind a mandatory user-consent safety gate, run `PreviewImpactAsync` on the proposed remediation tool, and capture before/after JSON snapshots using paired `Tier0_ReadOnly` verification tools upon user-consented execution.

---

## DECISION-021: Phase 4 Transparent ReAct UI & Live Dry-Run Review (`8c253b8`)

- **Date:** September 2026
- **Status:** Implemented (`exp-agentic-rebuild`)

### Context

The frontend previously displayed static hardcoded copy from a 390-line `ACTION_COPY` lookup table and bypassed backend previews for memory actions.

### Decision

Rewire `autonomyService.ts` and `AutonomyRemediationPanel.tsx` to:
1. Subscribe to `ReceiveReActStep` and `ReceiveProgress` over SignalR (`/hubs/remediation`).
2. Render a live `ReActTimeline` showing every `Thought`, `ToolCall`, `Observation` (with expandable raw JSON telemetry), `DryRunPreview`, `Execution`, and `Verification` step.
3. Display real dry-run measurements (`affectedItemsCount`, `estimatedBytesAffected`, `affectedTargets`, privilege checks) and allow switching between registered remediation tools with live dry-run recalculation.
