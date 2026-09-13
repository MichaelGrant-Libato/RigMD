# System Architecture

## Overview
RigMD is a React + C#/.NET diagnostic and controlled remediation platform for Windows PCs and Laptops. This document describes the current system architecture.

## Major Application Layers

1. **Desktop Shell (WPF + WebView2)**
   - **Responsibilities**: Provides a standalone native Windows window to host the application. Manages the lifecycle of the internal web server. Bundles the React production build inside `wwwroot/`.
   - **Key Technologies**: WPF, WebView2.
   - **Project**: `RigMD.Desktop`

2. **Presentation Layer (React + Vite)**
   - **Responsibilities**: Renders the UI, manages client-side state, handles user intake, and presents diagnostic results and history.
   - **Key Technologies**: React, TypeScript, Tailwind CSS, Vite.
   - **Project**: `frontend/`

3. **API Layer (ASP.NET Core)**
   - **Responsibilities**: Exposes HTTP endpoints (Controllers) to the React frontend. Handles routing, request validation, dependency injection, and serving the static React production build. Hosts a SignalR Hub for real-time progress streaming during remediation execution.
   - **Key Technologies**: .NET 10, ASP.NET Core, SignalR.
   - **Project**: `RigMD.Api`
   - **Controllers**:
     - `DiagnosticController` — symptom submission, automatic/local diagnosis, session retrieval, resolution checking
     - `RemediationController` — remediation action listing, action execution, verification target opening
     - `AutonomyController` — autonomous orchestration (plan, execute, dry-run)
     - `RecurringController` — recurring pattern analysis
     - `AgentController` — remote agent management, heartbeats, commands, snapshots
     - `HardwareController` — live hardware profile endpoint
     - `WarningSignsController` — warning sign analysis
     - `DashboardController` — dashboard metrics
     - `ProfilesController` — saved hardware profiles
     - `DatabaseController` — database management

4. **Application & Domain Layer (C#)**
   - **Responsibilities**: Contains the core business logic, the diagnostic engine, the autonomous remediation orchestration, and domain models.
   - **Key Technologies**: Pure C#, Pattern Matching.
   - **Projects**: `RigMD.Application`, `RigMD.Domain`
   - **Key Services**:
     - `DiagnosticEngineService` — orchestrates symptom-to-diagnosis pipeline
     - `AutomaticDiagnosisService` — deterministic rule-based diagnosis (component, scenario, full modes)
     - `RecurringPatternService` — detects recurring diagnostic patterns across sessions
     - `ResolutionService` — checks whether a diagnosis has been resolved
     - `WarningSignService` — normalizes and analyzes warning signs
     - `AutonomousOrchestrator` — closed-loop remediation execution
     - `RemediationPlanner` — generates remediation plans from diagnosis categories
     - `RemediationRegistry` — maps action IDs to executable remediation actions
     - `SafetyPolicy` — enforces safety tier restrictions on remediation plans
     - `PivotEngine` — handles pivot-to-next-action on remediation failure
     - `DryRunRemediationExecutor` — simulates remediation for testing

5. **Infrastructure & Windows Integration Layer (C#)**
   - **Responsibilities**: Interfaces with the underlying Windows OS and external services. Retrieves hardware telemetry directly via Windows APIs (WMI) and manages external AI API calls (Gemini).
   - **Key Technologies**: `System.Management`, `System.Diagnostics`, `HttpClient`.
   - **Project**: `RigMD.Infrastructure`
   - **Hardware Providers** (WMI-based, implementing contracts in `IHardwareProvider.cs`):
     - `WmiCpuProvider` — CPU name, usage, cores, threads, frequency, thermal throttling
     - `WmiGpuProvider` — GPU name, driver, type (Dedicated/Integrated), VRAM
     - `WmiMemoryProvider` — RAM total, used, usage percentage
     - `WmiStorageProvider` — Storage drives, types (NVMe/SATA/HDD), SMART status, volumes
     - `WmiMotherboardProvider` — Motherboard/chipset product name
     - `WmiOperatingSystemProvider` — OS version, device name, system age
     - `WindowsNetworkProvider` — Network adapter, IPv4, gateway, DNS resolution
     - `ProcessProvider` — Browser/game detection, top memory apps, memory leak warnings
     - `WmiDeviceTypeProvider` — Chassis type detection (Desktop, Laptop, Notebook, Tablet, etc.)
     - `WmiBatteryProvider` — Battery presence, charge %, status (Charging/Discharging/Critical), estimated run time
   - **AI Integration**:
     - `GeminiAiExplainer` — calls Google Gemini API for natural-language diagnostic explanations
     - `OfflineAiExplainer` — provides deterministic offline explanations when Gemini is unavailable
   - **Remediation Actions**:
     - `ClearTempFilesAction`, `ClearBrowserCacheAction`, `ClearWindowsUpdateCacheAction`
     - `FlushDnsAction`, `RunDiskCleanupAction`, `RunSfcScanAction`
   - **Remediation Infrastructure**:
     - `WindowsRemediationExecutor` — executes real remediation actions on the host system
     - `VerificationService` — verifies whether remediation resolved the issue
     - `RollbackManager` — handles rollback of failed remediation actions

6. **Persistence Layer (EF Core + Hybrid Database)**
   - **Responsibilities**: Manages local data storage to ensure offline functionality and handles optional cloud synchronization for cross-device telemetry.
   - **Key Technologies**: Entity Framework Core, SQLite (Local), Supabase PostgreSQL (Cloud).
   - **Repositories**:
     - `DiagnosticSessionRepository` — diagnostic session CRUD and remediation history
     - `AgentRepository` — remote agent management, commands, hardware snapshots
     - `RemediationRepository` — remediation action logging
   - **Services**:
     - `DatabaseSyncService` — synchronizes local SQLite with remote Supabase PostgreSQL
     - `LocalDatabaseSchemaUpgradeService` — applies schema migrations to local SQLite

7. **Remote Agent (Windows Service)**
   - **Responsibilities**: Runs as a background Windows Service on remote machines. Polls the API for commands, executes hardware scans, and reports results back.
   - **Key Technologies**: .NET Generic Host, `IHostedService`.
   - **Project**: `RigMD.Agent`
   - **Scan Tools**: CPU, GPU, Memory, Storage, Process, and full System Profile scans.

## Real-Time Communication (SignalR)

The API layer hosts a SignalR Hub (`/hubs/remediation`) that streams live progress from remediation actions to connected frontend clients.

```
AutonomyController
    ↓ progressReporter callback
AutonomousOrchestrator
    ↓ progressReporter callback
WindowsRemediationExecutor
    ↓ progressReporter callback
Remediation Action (e.g. ClearTempFilesAction)
    ↓ progressReporter("[CLEANUP] Deleted 500 files...")
AutonomyController (lambda)
    ↓ IHubContext<RemediationHub>.Clients.All.SendAsync("ReceiveProgress", msg)
SignalR Hub → WebSocket → React Frontend
```

The frontend subscribes to the `ReceiveProgress` event using `@microsoft/signalr` and renders progress in a live terminal UI within the `AutonomyRemediationPanel` component.

## Data Flow

1. **Intake**: React UI submits diagnostic symptoms and context to the ASP.NET Core API.
2. **Telemetry Collection**: The Application Layer requests a live hardware profile from the Infrastructure Layer (via native Windows WMI APIs). This includes device type detection (Desktop vs Laptop) and battery health data for laptops.
3. **Engine Evaluation**: The Domain Layer's diagnostic engine cross-references the symptoms with the hardware telemetry to categorize the issue and determine an action plan.
4. **Persistence**: The resulting `DiagnosticSession` is saved to the local SQLite database. 
5. **Sync**: A background service asynchronously pushes unsynced records to Supabase when network connectivity is available.

## Device-Aware Diagnostics

RigMD automatically detects whether it is running on a Desktop PC or a Laptop via `Win32_SystemEnclosure` chassis type detection. This context enables device-specific diagnostic reasoning:

- **Desktop**: Thermal advice focuses on case fans, airflow, and dust buildup.
- **Laptop**: Thermal advice focuses on cooling pads, vent obstruction, battery health, and power plan settings.
- **Battery-Aware**: On laptops, battery status (charging, discharging, critical) is factored into performance diagnosis, as power throttling and thermal throttling behave differently on battery vs AC power.

## Target Behavior: Controlled Autonomous Remediation
The system transitions from purely advisory output to controlled autonomous remediation.
See `AUTONOMOUS_ENGINE.md` and `REMEDIATION_POLICY.md` for safety boundaries, rollback procedures, and action escalation paths.
