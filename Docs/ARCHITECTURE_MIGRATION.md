# Architecture Migration Plan

## 1. Goal & Rationale
RigMD is undergoing a major architectural and behavioral migration to better integrate with the Windows OS and expand its capabilities from a pure advisory system to a controlled autonomous remediation engine.

## 2. Alignment Matrix

| Aspect | Old Baseline | Target New State |
|--------|--------------|------------------|
| **Core Languages/Frameworks** | Python + CustomTkinter (or FastAPI) | React + C# / .NET (ASP.NET Core) |
| **Storage Strategy** | SQLite (Pure Local) or Supabase (Pure Cloud) | Hybrid: SQLite (Local Source of Truth) + Supabase (Cloud Sync) |
| **Hardware Telemetry** | Python wrappers (`psutil`, `pywin32`) | Native C# Windows APIs (`System.Management`, `CIM`) |
| **System Behavior** | Advisory / Manual Action (Decision Support) | Controlled Autonomous Remediation (Verify / Rollback / Pivot) |

## 3. Execution Phases

### Phase 0: Baseline & Alignment (Current)
- Establish target architecture documentation.
- Define autonomous engine rules and remediation policies.
- Ensure all developers are aligned on the C# migration before modifying code.

### Phase 1: Foundation & Domain (C#)
- Setup the .NET Solution with a Clean Architecture folder structure.
- Define core Domain entities and business rules in C#.
- Port diagnostic engine logic.

### Phase 2: Infrastructure & Local Data (C#)
- Implement `LocalDbContext` using EF Core + SQLite.
- Build Windows Native integration services for hardware telemetry.

### Phase 3: Autonomous Engine Integration
- Implement the controlled remediation engine (Verification, Execution, Rollback).
- Integrate AI (Gemini) for dynamic explanations and pivot strategies.

### Phase 4: API & Frontend Polish
- Connect the React frontend to the new ASP.NET Core API.
- Update UI to reflect autonomous remediation states and rollback options.

### Phase 5: Cloud Sync
- Implement the background sync worker for Supabase.

*(Note: Official academic thesis documents like the SRS and SDD should only be formally updated once these architectural targets have been validated and adopted by the team.)*
