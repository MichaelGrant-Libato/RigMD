# RigMD Project Topology

This document explicitly defines the boundaries and purposes of each directory in the RigMD project. It serves as the single source of truth for where code should be placed. 

These architectural boundaries are strictly enforced via unit tests in `RigMD.Tests/Architecture`.

---

## Backend Directory Structure (`backend-dotnet/`)

The backend follows a strict Clean Architecture pattern.

### `RigMD.Domain/`
- **Purpose**: Core business rules, entities, enums, and the diagnostic engine logic.
- **Allowed Dependencies**: None. (Pure C#)
- **What goes here**: Data models (e.g., `DiagnosticSession`, `SystemProfile`), Enums, Business Rules.
- **What DOES NOT go here**: Database logic, external API calls, Windows-specific APIs.

### `RigMD.Application/`
- **Purpose**: Use cases and application logic that orchestrates domain objects.
- **Allowed Dependencies**: `RigMD.Domain`.
- **What goes here**: Services (e.g., `DiagnosticEngineService`), DTOs, and Interfaces for Providers (`IHardwareProvider`).
- **What DOES NOT go here**: Actual implementation of hardware providers or database persistence.

### `RigMD.Infrastructure/`
- **Purpose**: External system integrations (Windows APIs, Databases, external AI calls).
- **Allowed Dependencies**: `RigMD.Application`, `RigMD.Domain`.
- **What goes here**: Entity Framework DB Context, SQLite logic, WMI Providers, Remediation Actions (`ClearTempFilesAction`), Gemini API integration.
- **What DOES NOT go here**: Core business rules.

#### Sub-directory Rules:
- `Infrastructure/Windows/`: Any code using `System.Management` (WMI) or `System.Diagnostics` (Processes).
- `Infrastructure/Persistence/`: Any code using Entity Framework (`RigMdDbContext`, Repositories).
- `Infrastructure/Remediation/Actions/`: Every individual remediation action must go here and implement `IRemediationAction`.

### `RigMD.Api/`
- **Purpose**: Presentation layer for external clients (React Frontend, Agent).
- **Allowed Dependencies**: `RigMD.Application`, `RigMD.Infrastructure` (only for Dependency Injection setup in `Program.cs`).
- **What goes here**: Controllers, Minimal APIs, SignalR Hubs.
- **What DOES NOT go here**: Business logic, database queries.
- **Strict Rule**: Classes in the `RigMD.Api.Controllers` namespace are **NOT ALLOWED** to directly reference classes in `RigMD.Infrastructure`. All work must be delegated to services in `RigMD.Application`.

### `RigMD.Agent/`
- **Purpose**: The background Windows Service deployed to end-user machines.
- **Allowed Dependencies**: `RigMD.Application`, `RigMD.Infrastructure`.
- **What goes here**: Worker services, Background tasks, Agent specific configuration.

### `RigMD.Desktop/`
- **Purpose**: The native Windows shell for local execution.
- **Allowed Dependencies**: `RigMD.Api`.
- **What goes here**: WPF Windows, WebView2 configuration.

---

## Frontend Directory Structure (`frontend/`)

The frontend is a React + Vite application. 

### `src/components/`
- **Purpose**: Reusable UI elements or specific feature panels.
- **What goes here**: Buttons, dialogs, `AppSidebar.tsx`, `AutonomyRemediationPanel.tsx`.

### `src/pages/`
- **Purpose**: Full-page views that correspond to routes.
- **What goes here**: `HardwareDashboard.tsx`, `NewDiagnosisView.tsx`.
- **Rule**: Pages should coordinate data fetching and pass props down to components. Components should ideally be stateless where possible.

### `src/services/` (If applicable)
- **Purpose**: API clients and data fetching logic.
- **What goes here**: Axios/Fetch wrappers that talk to the `.NET` backend.

---

## Modifying This Document
If you add a new top-level concept or directory, you must document its purpose and dependency rules here before merging into `main`.
