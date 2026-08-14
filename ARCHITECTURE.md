# System Architecture

## Overview
RigMD is evolving from a Python-based advisory tool to a React + C#/.NET diagnostic system capable of controlled autonomous remediation. This document describes the new target architecture.

## Major Application Layers

1. **Presentation Layer (React + Vite)**
   - **Responsibilities**: Renders the UI, manages client-side state, handles user intake, and presents diagnostic results and history.
   - **Key Technologies**: React, TypeScript, Tailwind CSS.

2. **API Layer (ASP.NET Core)**
   - **Responsibilities**: Exposes HTTP endpoints (Minimal APIs/Controllers) to the React frontend. Handles routing, basic request validation, and dependency injection.
   - **Key Technologies**: .NET 8/9, ASP.NET Core.

3. **Application & Domain Layer (C#)**
   - **Responsibilities**: Contains the core business logic, the diagnostic engine, the autonomous remediation orchestration, and domain models (e.g., `DiagnosticSession`, `Profile`).
   - **Key Technologies**: Pure C#, Pattern Matching.

4. **Infrastructure & Windows Integration Layer (C#)**
   - **Responsibilities**: Interfaces with the underlying Windows OS and external services. Retrieves hardware telemetry directly via Windows APIs (WMI/CIM/EventLogs) and manages external AI API calls (Gemini).
   - **Key Technologies**: `System.Management`, `System.Diagnostics`, `HttpClient`.

5. **Persistence Layer (EF Core + Hybrid Database)**
   - **Responsibilities**: Manages local data storage to ensure offline functionality and handles optional cloud synchronization for cross-device telemetry.
   - **Key Technologies**: Entity Framework Core, SQLite (Local), Supabase PostgreSQL (Cloud).

## Data Flow

1. **Intake**: React UI submits diagnostic symptoms and context to the ASP.NET Core API.
2. **Telemetry Collection**: The Application Layer requests a live hardware profile from the Infrastructure Layer (via native Windows APIs).
3. **Engine Evaluation**: The Domain Layer's diagnostic engine cross-references the symptoms with the hardware telemetry to categorize the issue and determine an action plan.
4. **Persistence**: The resulting `DiagnosticSession` is saved to the local SQLite database. 
5. **Sync**: A background worker asynchronously pushes unsynced records to Supabase when network connectivity is available.

## Target Behavior: Controlled Autonomous Remediation
The system transitions from purely advisory output to controlled autonomous remediation.
See `AUTONOMOUS_ENGINE.md` and `REMEDIATION_POLICY.md` for safety boundaries, rollback procedures, and action escalation paths.
