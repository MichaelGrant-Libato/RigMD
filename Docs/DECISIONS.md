# Architectural Decisions Record (ADR)

## DECISION-001: Migration to C# / ASP.NET Core
- **Date**: August 2026
- **Context**: Python hardware wrappers (`psutil`, `pywin32`) lack the depth, speed, and reliability required for a robust Windows desktop diagnostic tool.
- **Decision**: Migrate the backend layer to C# / .NET (ASP.NET Core).
- **Reason**: Provides native access to `System.Management`, WMI, CIM, and Windows Event Logs, and allows compilation into a standalone Windows executable.
- **Consequences**: Requires porting existing Python logic; deprecates the `backend/` Python directory.

## DECISION-002: Controlled Autonomous Remediation
- **Date**: August 2026
- **Context**: The original thesis scope targeted an "advisory/decision-support" tool. We aim to increase utility by actually resolving detected issues.
- **Decision**: Evolve the system behavior to include controlled autonomous remediation.
- **Reason**: Automating safe fixes (e.g., service restarts, network resets) significantly improves the user experience.
- **Consequences**: Requires implementing robust Verification, Rollback, and Pivot logic to prevent system degradation. Formal thesis documents (SRS/SDD) will need eventual updating to reflect this expanded scope.

## DECISION-003: Hybrid Local/Cloud Database Architecture
- **Date**: August 2026
- **Context**: Diagnostics often occur when a PC is unstable or offline (e.g., network driver crash). A purely cloud-based database (Supabase) causes the app to fail when it is needed most.
- **Decision**: Implement SQLite as the local "Source of Truth" with Entity Framework Core, and sync to Supabase asynchronously.
- **Reason**: Guarantees 100% offline functionality.
- **Consequences**: Requires implementing a background sync worker and conflict resolution logic.
