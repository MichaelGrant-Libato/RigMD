# System Architecture

## Overview
RigMD is a local-first React + C#/.NET 10 diagnostic and ReAct-driven autonomous remediation platform for Windows PCs and Laptops. This document describes the active system architecture following the 4-Phase Agentic Re-Structuring (`exp-agentic-rebuild`).

---

## Major Application Layers

1. **Desktop Shell (WPF + WebView2)**
   - **Responsibilities**: Provides a standalone native Windows window to host the application, manages the lifecycle of the internal ASP.NET Core web server, and bundles the React production build inside `wwwroot/`.
   - **Key Technologies**: WPF, WebView2.
   - **Project**: `RigMD.Desktop`

2. **Presentation Layer (React + Vite)**
   - **Responsibilities**: Renders the hardware dashboard, symptom intake, diagnostic results, and the real-time **ReAct Agent Console & Remediation Review UI** (`AutonomyRemediationPanel.tsx`). Subscribes to SignalR WebSocket streams (`ReceiveReActStep` and `ReceiveProgress`) to render live `Thought -> Tool Call -> Observation -> Dry-Run Preview -> Execution -> Verification` traces.
   - **Key Technologies**: React, TypeScript, Tailwind CSS, Vite, `@microsoft/signalr`.
   - **Project**: `frontend/`

3. **API Layer (ASP.NET Core)**
   - **Responsibilities**: Exposes REST endpoints to the React frontend, manages dependency injection, serves the static React production bundle from `wwwroot/`, and hosts SignalR Hubs for real-time telemetry and ReAct step streaming.
   - **Key Technologies**: .NET 10, ASP.NET Core, SignalR.
   - **Project**: `RigMD.Api`
   - **Controllers**:
     - `AutonomyController` — ReAct dry-run investigation & preview (`POST /api/autonomy/preview`), user-approved tool execution & before/after verification (`POST /api/autonomy/execute`), tool catalog (`GET /api/autonomy/tools`), per-tool dry-run preview (`POST /api/autonomy/tools/preview`), direct tool execution (`POST /api/autonomy/tools/execute`), memory-heavy process termination (`GET /api/autonomy/memory-apps`, `POST /api/autonomy/close-selected-app`), and Windows verification tool launching (`POST /api/autonomy/open-target` / `POST /api/remediation/open-target`).
     - `DiagnosticController` — symptom intake, automatic/local baseline diagnosis, session retrieval, and resolution checking.
     - `HardwareController` — live hardware profile (`GET /api/hardware/live`), cache refresh (`POST /api/hardware/refresh`), and instant telemetry snapshots (`GET /api/hardware/snapshot`).
     - `RecurringController` — recurring pattern analysis across sessions.
     - `WarningSignsController` — warning sign catalog and occurrence analysis.
     - `DashboardController` — dashboard summary metrics.
     - `ProfilesController` — saved hardware profiles.
     - `DatabaseController` — SQLite and optional cloud sync health checks.
   - **SignalR Hubs**:
     - `RemediationHub` (`/hubs/remediation`) — streams `ReceiveReActStep` and `ReceiveProgress` events in real time.
     - `TelemetryHub` (`/hubs/telemetry`) — broadcasts live hardware sensor ticks.

4. **Application & Domain Layer (C#)**
   - **Responsibilities**: Contains core domain entities, baseline telemetry mappers, tool/ReAct contracts, and the `AutonomousOrchestrator` ReAct loop. Enforces strict inward dependency rules (`Domain` <- `Application` <- `Infrastructure` / `Api`, verified by `LayerDependencyTests`).
   - **Projects**: `RigMD.Application`, `RigMD.Domain`
   - **Key Contracts & Services**:
     - `IRigMdAgentTool` & `IRigMdAgentToolRegistry` — standardized interface and registry for all 14 diagnostic (`Tier0_ReadOnly`) and remediation (`Tier1_SafeReversible`, `Tier2_DestructiveOrAdmin`) tools, including JSON Schema function declarations (`AgentToolFunctionDeclaration`) and non-destructive `PreviewImpactAsync`.
     - `IReActLlmClient` — contract for multi-turn ReAct decision steps (`DecideNextTurnAsync`).
     - `AutonomousOrchestrator` — drives the multi-turn `Thought -> Tool Call -> Observation` loop, enforces the safety gate preventing unapproved write-tool execution during reasoning, runs live dry-run impact previews, and performs before/after telemetry verification using paired `Tier0_ReadOnly` tools.
     - `DiagnosticEngineService` & `AutomaticDiagnosisService` — lightweight baseline telemetry mappers that record initial session state before handing off deep investigation to the ReAct agent.
     - `RecurringPatternService`, `ResolutionService`, `WarningSignService` — session history and resolution tracking services.

5. **Infrastructure & Windows Integration Layer (C#)**
   - **Responsibilities**: Interfaces directly with Windows WMI, CIM, `LibreHardwareMonitorLib`, `System.Diagnostics`, Windows Event Logs (`wevtutil.exe`), and the Google Gemini Function-Calling API.
   - **Project**: `RigMD.Infrastructure`
   - **Hardware & Sensor Providers** (implementing `IHardwareProvider.cs`):
     - `HardwareMonitorService` (`LibreHardwareMonitorLib`) — real-time CPU/GPU package & core temperatures, loads, and dedicated VRAM sensors.
     - `WmiCpuProvider`, `WmiGpuProvider`, `WmiMemoryProvider`, `WmiStorageProvider`, `WmiMotherboardProvider`, `WmiOperatingSystemProvider`, `WindowsNetworkProvider`, `ProcessProvider`, `WmiDeviceTypeProvider`, `WmiBatteryProvider`, `WmiPowerProvider`, `WmiDisplayProvider`, `WindowsSystemProfileService`.
   - **ReAct LLM Reasoning Client**:
     - `GeminiReActLlmClient` — invokes Google Gemini (`gemini-2.5-flash` / `gemini-2.0-flash`) free-tier native `functionDeclarations` when `Gemini:ApiKey` or `GEMINI_API_KEY` is configured, and automatically falls back to a built-in **$0.00 Local Tool-Calling ReAct Engine** that invokes the same `Tier0_ReadOnly` tools and synthesizes root-cause proposals from live JSON observations.
   - **14 Registered Agent Tools (`RigMD.Infrastructure/Remediation/Tools/`)**:
     - **7 `Tier0_ReadOnly` Diagnostic Tools**:
       1. `InspectCpuAndThermalsTool` (`inspect_cpu_and_thermals`)
       2. `InspectMemoryAndProcessesTool` (`inspect_memory_and_processes`)
       3. `InspectStorageHealthTool` (`inspect_storage_health`)
       4. `InspectGpuAndDisplaysTool` (`inspect_gpu_and_displays`)
       5. `InspectNetworkConnectivityTool` (`inspect_network_connectivity`)
       6. `InspectBatteryAndPowerTool` (`inspect_battery_and_power`)
       7. `QueryWindowsEventLogsTool` (`query_windows_event_logs`)
     - **7 `Tier1_SafeReversible` & `Tier2_DestructiveOrAdmin` Remediation Tools**:
       1. `ClearTempFilesTool` (`clear_temp_files` — Tier 1)
       2. `FlushDnsCacheTool` (`flush_dns_cache` — Tier 1)
       3. `RestartWindowsExplorerTool` (`restart_windows_explorer` — Tier 1)
       4. `TerminateProcessesTool` (`terminate_processes` — Tier 2)
       5. `ClearBrowserCacheTool` (`clear_browser_cache` — Tier 2)
       6. `ClearWindowsUpdateCacheTool` (`clear_windows_update_cache` — Tier 2)
       7. `RunSystemFileCheckerTool` (`run_system_file_checker` — Tier 2)
   - **Real OS Action Primitives (`RigMD.Infrastructure/Remediation/Actions/`)**:
     - `ClearTempFilesAction`, `ClearBrowserCacheAction`, `ClearWindowsUpdateCacheAction`, `FlushDnsAction`, `RestartExplorerAction`, `TerminateProcessesAction`, `RunDiskCleanupAction`, `RunSfcScanAction`, dispatched via `WindowsRemediationExecutor` and the `IRigMdAgentTool` wrappers.

6. **Persistence Layer (EF Core + Local-First SQLite)**
   - **Responsibilities**: Stores all diagnostic sessions, hardware profiles, and remediation audit runs locally in `%LOCALAPPDATA%\RigMD\rigmd.db` via Entity Framework Core SQLite, with optional startup synchronization from Supabase PostgreSQL when `DATABASE_URL` is configured.
   - **Repositories**: `DiagnosticSessionRepository`, `RemediationRepository`.

---

## Real-Time ReAct Streaming Architecture (SignalR)

During both **Investigation/Preview** (`POST /api/autonomy/preview`) and **Execution/Verification** (`POST /api/autonomy/execute`), the backend streams structured `ReActTraceStep` objects and progress lines to connected clients over `/hubs/remediation`:

```
AutonomyController
    ↓ stepReporter (ReceiveReActStep) & progressReporter (ReceiveProgress)
AutonomousOrchestrator (ReAct Loop)
    ├── Turn 1..N: IReActLlmClient.DecideNextTurnAsync(...)
    │       ↓ [THOUGHT] emitted to SignalR
    ├── Tier 0 Tool Call: IRigMdAgentTool.ExecuteAsync(...)
    │       ↓ [TOOL CALL] & [OBSERVATION] (with live WMI/OS JSON) emitted to SignalR
    ├── Safety Gate & Dry-Run Preview: IRigMdAgentTool.PreviewImpactAsync(...)
    │       ↓ [DRY-RUN PREVIEW] & [AWAITING APPROVAL] emitted to SignalR
    └── User-Consented Execution:
            ├── Pre-Execution Baseline Snapshot (paired Tier 0 tool)
            ├── Real OS Tool Execution (Tier 1 / Tier 2 tool)
            └── Post-Execution Verification Snapshot (paired Tier 0 tool)
```

---

## Retired Legacy Subsystems (Purged in Phase 1)
The following legacy or simulated components were permanently removed during the `exp-agentic-rebuild` restructuring (-16,630 lines):
- **Legacy Python Backend (`backend/`)**: Completely removed; 100% of functionality resides in `backend-dotnet/`.
- **Cloud Polling Agent Queue (`RigMD.Agent`, `AgentController`, `AgentRepository`)**: Removed; RigMD runs locally on the target Windows machine with direct WMI and OS access.
- **Hardcoded Stubs & Fake Autonomy Classes**: `RemediationPlanner`, `RemediationRegistry`, `SafetyPolicy`, `PivotEngine`, `DryRunRemediationExecutor` (which returned `Thread.Sleep` + fake strings), `RollbackManager` (no-op stub), and `VerificationService` (arbitrary file-count check) were replaced by real `IRigMdAgentTool.PreviewImpactAsync` dry-run measurements and paired `Tier0_ReadOnly` before/after telemetry verification.
