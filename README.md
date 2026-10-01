# RigMD — Windows PC & Laptop Diagnostic & Remediation System

RigMD is a Windows desktop diagnostic advisory and controlled remediation platform for PCs and Laptops.

---

## 1. Overview & Vision

RigMD is a **controlled closed-loop diagnostic and remediation platform** for Windows desktop PCs and laptops.

The system combines:

1. **Native Windows Telemetry**
   Hardware, drivers, processes, utilization, battery health, device type detection, and Windows system information collected via WMI.

2. **Deterministic Diagnostic Engine**
   Structured scoring, evidence, action categories, confidence levels, recurring-pattern analysis, and warning-sign detection.

3. **Controlled Remediation Engine**
   Approved remediation actions, safety checks, resolution rechecking, verification, rollback, and pivot behavior.

4. **Offline-First Persistence**
   Local SQLite as the source of truth, with optional Supabase PostgreSQL synchronization.

5. **Constrained AI Explanations**
   AI (Google Gemini) may explain or summarize diagnostic results, but does not control system execution.

---

## 2. Current Technology Stack

| Layer | Implementation |
|---|---|
| Frontend | React + TypeScript + Vite + Tailwind CSS |
| Backend API | C# / ASP.NET Core (.NET 10) |
| Desktop Shell | WPF + WebView2 (Auto-spawns and binds local API) |
| Hardware Telemetry | 100% User-Mode Windows Telemetry (WMI, Performance Counters, ACPI, Thermal Fallback — Driver-Free) |
| Diagnostic Engine | Deterministic C# Domain layer with strict component/scenario scoping |
| Persistence | SQLite (Local Source of Truth with WAL mode) & Supabase PostgreSQL (Optional Sync) |
| Remediation | 3-Tier Controlled Autonomous Remediation Engine with Dry-Run Previews |
| Real-Time Streaming | SignalR WebSocket Hub (live telemetry and ReAct trace streaming) |
| AI Integration | Multi-Turn ReAct Reasoning Agent (Google Gemini with Built-In Offline Fallback) |
| Local Agent / Worker | In-Process Background Worker (Hosted in `RigMD.Api`; no separate Windows service required) |

---

## 3. Repository Structure

```
RigMD/
│
├── backend-dotnet/
│   │
│   ├── RigMD.slnx
│   │
│   ├── RigMD.Api/
│   │   ├── Controllers/
│   │   │   ├── AutonomyController.cs
│   │   │   ├── DashboardController.cs
│   │   │   ├── DatabaseController.cs
│   │   │   ├── DiagnosticController.cs
│   │   │   ├── HardwareController.cs
│   │   │   ├── ProfilesController.cs
│   │   │   ├── RecurringController.cs
│   │   │   └── WarningSignsController.cs
│   │   ├── Hubs/
│   │   │   ├── RemediationHub.cs
│   │   │   └── TelemetryHub.cs
│   │   ├── Services/
│   │   │   └── TelemetryBackgroundService.cs
│   │   └── Program.cs
│   │
│   ├── RigMD.Application/
│   │   ├── Contracts/
│   │   │   ├── Ai/
│   │   │   ├── Autonomy/
│   │   │   ├── Common/
│   │   │   ├── Persistence/
│   │   │   └── Providers/
│   │   ├── Models/
│   │   │   ├── AgentReActModels.cs
│   │   │   └── AgentToolModels.cs
│   │   └── Services/
│   │       ├── Autonomy/
│   │       │   ├── AutonomousOrchestrator.cs
│   │       │   └── DiagnosticScopeMapper.cs
│   │       ├── AutomaticDiagnosisService.cs
│   │       ├── DiagnosticEngineService.cs
│   │       ├── RecurringPatternService.cs
│   │       ├── ResolutionService.cs
│   │       ├── RigMdAgentRuntimeSettingsStore.cs
│   │       └── WarningSignService.cs
│   │
│   ├── RigMD.Domain/
│   │   ├── Entities/
│   │   └── Rules/
│   │       └── DiagnosticEngine.cs
│   │
│   ├── RigMD.Infrastructure/
│   │   ├── Ai/
│   │   │   ├── GeminiAiExplainer.cs
│   │   │   ├── GeminiReActLlmClient.cs
│   │   │   └── OfflineAiExplainer.cs
│   │   ├── Persistence/
│   │   │   ├── DatabaseSyncService.cs
│   │   │   ├── DiagnosticSessionRepository.cs
│   │   │   ├── LocalDatabaseSchemaUpgradeService.cs
│   │   │   ├── RemediationRepository.cs
│   │   │   └── RigMdDbContext.cs
│   │   ├── Remediation/
│   │   │   ├── Actions/
│   │   │   ├── Tools/
│   │   │   │   ├── Diagnostic/
│   │   │   │   │   ├── InspectBatteryAndPowerTool.cs
│   │   │   │   │   ├── InspectCpuAndThermalsTool.cs
│   │   │   │   │   ├── InspectFullDeviceProfileTool.cs
│   │   │   │   │   ├── InspectGpuAndDisplaysTool.cs
│   │   │   │   │   ├── InspectMemoryAndProcessesTool.cs
│   │   │   │   │   ├── InspectNetworkConnectivityTool.cs
│   │   │   │   │   ├── InspectStorageHealthTool.cs
│   │   │   │   │   ├── QueryPastChecksAndRemediationsTool.cs
│   │   │   │   │   ├── QueryRecurringProblemsAndWarningsTool.cs
│   │   │   │   │   ├── QueryStartupAppsTool.cs
│   │   │   │   │   └── QueryWindowsEventLogsTool.cs
│   │   │   │   ├── Remediation/
│   │   │   │   │   ├── ClearBrowserCacheTool.cs
│   │   │   │   │   ├── ClearTempFilesTool.cs
│   │   │   │   │   ├── ClearWindowsUpdateCacheTool.cs
│   │   │   │   │   ├── FlushDnsCacheTool.cs
│   │   │   │   │   ├── RestartWindowsExplorerTool.cs
│   │   │   │   │   ├── RunSystemFileCheckerTool.cs
│   │   │   │   │   └── TerminateProcessesTool.cs
│   │   │   │   └── RigMdAgentToolRegistry.cs
│   │   │   └── WindowsRemediationExecutor.cs
│   │   └── Windows/
│   │       ├── HardwareMonitorService.cs
│   │       ├── ProcessProvider.cs
│   │       ├── WindowsNetworkProvider.cs
│   │       ├── WindowsSystemProfileService.cs
│   │       ├── WmiBatteryProvider.cs
│   │       ├── WmiCpuProvider.cs
│   │       ├── WmiDeviceTypeProvider.cs
│   │       ├── WmiGpuProvider.cs
│   │       ├── WmiMemoryProvider.cs
│   │       ├── WmiMotherboardProvider.cs
│   │       ├── WmiOperatingSystemProvider.cs
│   │       └── WmiStorageProvider.cs
│   │
│   ├── RigMD.Desktop/
│   │   ├── App.xaml.cs
│   │   ├── MainWindow.xaml.cs
│   │   └── wwwroot/
│   │
│   └── RigMD.Tests/
│       ├── Application/
│       ├── Services/
│       └── Domain/
│
├── frontend/
│   └── React + TypeScript + Vite + Tailwind CSS
│       ├── src/
│       │   ├── components/
│       │   │   ├── AppSidebar.tsx
│       │   │   ├── AutonomyRemediationPanel.tsx
│       │   │   ├── HomeDashboardContent.tsx
│       │   │   ├── SplashScreen.tsx
│       │   │   ├── TopHeader.tsx
│       │   │   └── ...
│       │   └── pages/
│       │       ├── NewDiagnosisView.tsx
│       │       ├── DiagnosticHistoryView.tsx
│       │       ├── DiagnosticSessionDetailView.tsx
│       │       ├── RecurringPatternsView.tsx
│       │       ├── WarningSignsView.tsx
│       │       ├── HardwareDashboard.tsx
│       │       ├── SystemProfileView.tsx
│       │       ├── SettingsView.tsx
│       │       ├── HelpScopeView.tsx
│       │       └── ShareReportView.tsx
│       └── ...
│
├── installer/
│   └── Inno Setup installer scripts
│
├── Docs/
│   ├── ARCHITECTURE.md
│   ├── AUTONOMOUS_ENGINE.md
│   ├── DECISIONS.md
│   ├── MIGRATION_HISTORY.md
│   ├── REMEDIATION_POLICY.md
│   ├── Migration_Archive/
│   │   ├── ARCHITECTURE_MIGRATION.md
│   │   ├── BASELINE.md
│   │   ├── C_SHARP_MIGRATION_PLAN.md
│   │   ├── LEGACY_CAPABILITY_INVENTORY.md
│   │   └── MIGRATION_MATRIX.md
│   └── database/
│       └── SQL reference files
│
├── AGENTS.md
├── IMPLEMENT_ME.md
├── README.md
└── .gitignore
```

---

## 4. Controlled Remediation & Safety

RigMD employs a controlled, closed-loop remediation engine that executes explicitly approved actions, verifies resolutions, and safely handles rollbacks or pivots upon failure. Remediation actions are strictly classified into three safety tiers (Low-risk, Configuration-changing, and High-risk), ensuring that destructive or unverified operations require explicit user consent or remain advisory-only; see [`Docs/REMEDIATION_POLICY.md`](./Docs/REMEDIATION_POLICY.md) for full details.

### Currently Implemented Autonomous Actions

| Action | Description | Tier |
|---|---|---|
| Clear User Temp Files | Removes temporary files from user TEMP folder with pre-calculation | Tier 1 |
| Clear Browser Cache | Clears browser cache data safely | Tier 1 |
| Clear Windows Update Cache | Clears Windows Update download cache (`SoftwareDistribution\Download`) | Tier 1 |
| Flush DNS Cache | Resets the Windows DNS resolver cache (`ipconfig /flushdns`) | Tier 1 |
| Restart Windows Explorer | Refreshes the Windows taskbar, system tray, and explorer shell | Tier 1 |
| Run SFC Scan | Runs Windows System File Checker (`sfc /scannow`) in background | Tier 1 |
| Terminate Heavy Processes | Selectively terminates high-memory processes (protected system blocklist enforced) | Tier 1 |

*Every remediation action includes a **Dry-Run Impact Preview** (showing estimated space saved or apps affected) and a post-execution **Verification Check**.*

### Assisted (Non-Autonomous) Remediation Actions

| Action | What It Opens |
|---|---|
| Open Task Manager | `taskmgr.exe` |
| Open Device Manager | `devmgmt.msc` |
| Open Startup Apps | `ms-settings:startupapps` |
| Open Power Settings | `ms-settings:powersleep` |
| Open Reliability Monitor | `perfmon /rel` |
| Open Backup Settings | `ms-settings:backup` |
| Open Storage Settings | `ms-settings:storagesense` |
| Show GPU Reset Shortcut | Displays `Win+Ctrl+Shift+B` |
| Read-Only Disk Scan | `chkdsk C: /scan` (read-only) |

---

## 5. Hardware Telemetry Providers

RigMD collects live hardware telemetry via Windows Management Instrumentation (WMI) and standard Windows Performance Counters. All telemetry is **100% user-mode and driver-free** (no `WinRing0.sys` or external kernel drivers), ensuring zero false-positive antivirus warnings.

| Provider | Source / Method | Data Collected |
|---|---|---|
| `HardwareMonitorService` | ACPI thermal zones, Win32_PerfFormattedData, and thermal estimation | CPU temperature, GPU temperature, clock speed, thermal status |
| `WmiCpuProvider` | `Win32_Processor`, Performance Counters | Name, usage %, cores, threads, frequency, thermal throttling |
| `WmiGpuProvider` | `Win32_VideoController` | Name, driver version, type (Dedicated/Integrated), VRAM |
| `WmiMemoryProvider` | `Win32_OperatingSystem` | Total/used GB, usage % |
| `WmiStorageProvider` | `Win32_DiskDrive`, `Win32_LogicalDisk` | Drive models, types (NVMe/SATA/HDD), SMART status, volumes |
| `WmiMotherboardProvider` | `Win32_BaseBoard` | Chipset/product name |
| `WmiOperatingSystemProvider` | `Win32_OperatingSystem` | OS version, device name, true system install age |
| `WindowsNetworkProvider` | `System.Net.NetworkInformation` | Adapter name, IPv4, gateway, DNS resolution |
| `ProcessProvider` | `System.Diagnostics.Process` | Browser detection, game detection, top memory apps, memory leak warnings |
| `WmiDeviceTypeProvider` | `Win32_SystemEnclosure` | Chassis type (Desktop, Laptop, Notebook, Tablet, etc.) |
| `WmiBatteryProvider` | `Win32_Battery` | Battery presence, charge %, status (Charging/Discharging/Critical), estimated run time |

---

## 6. Running RigMD

### Requirements

Install:

- .NET 10 SDK (or compatible)
- Node.js
- npm
- Windows 10/11
- Valid local backend secrets/configuration

Do not commit:

- database passwords
- API keys
- Supabase credentials
- Gemini API keys
- real `.env` files

### Running RigMD in Development

RigMD operates with a streamlined, local-first architecture. The diagnostic and remediation engine runs directly inside `RigMD.Api`, and the compiled React UI is served automatically from `wwwroot`.

#### Option 1: Full Desktop App (Recommended)
Build and run the WPF WebView2 desktop application (which automatically starts and manages the local API as a child process):

```powershell
cd backend-dotnet
dotnet build
dotnet run --project RigMD.Desktop
```

#### Option 2: API & Browser Interface
Run the backend API directly in one terminal:

```powershell
cd backend-dotnet
dotnet run --project RigMD.Api
```
Then navigate to `http://localhost:5273` in any web browser.

#### Option 3: Live Frontend Development (Vite Hot-Reload)
When actively making React UI changes:

1. **Terminal 1 (Backend API):**
   ```powershell
   cd backend-dotnet
   dotnet run --project RigMD.Api
   ```
2. **Terminal 2 (Frontend Dev Server):**
   ```powershell
   cd frontend
   npm run dev
   ```
   Navigate to `http://localhost:5173`. UI changes will hot-reload instantly.

---

## 7. Build and Test

### Backend

```powershell
cd backend-dotnet
dotnet build
dotnet test
```

### Frontend

```powershell
cd frontend
npm run build
```

Current automated test coverage includes:

- ReAct Agent tool layer execution & argument validation
- Autonomous multi-turn ReAct reasoning loop
- Strict diagnostic mode scoping (Full vs. Scenario vs. Component)
- Hardware presence pre-flight gating (Battery, GPU, Display)
- Telemetry evaluation & baseline comparisons
- Scope-aware resolution rechecks (`ResolutionService`)
- SQLite schema migrations & WAL concurrency mode
- Warning sign normalization & recurring pattern detection

---

## 8. Documentation

- [Architecture](./Docs/ARCHITECTURE.md)
- [Autonomous Engine](./Docs/AUTONOMOUS_ENGINE.md)
- [Remediation Policy](./Docs/REMEDIATION_POLICY.md)
- [Architectural Decisions](./Docs/DECISIONS.md)
- [Migration History](./Docs/MIGRATION_HISTORY.md)
- [Architecture Migration (Archive)](./Docs/Migration_Archive/ARCHITECTURE_MIGRATION.md)
- [C# Migration Roadmap (Archive)](./Docs/Migration_Archive/C_SHARP_MIGRATION_PLAN.md)
- [Migration Matrix (Archive)](./Docs/Migration_Archive/MIGRATION_MATRIX.md)
- [Legacy Baseline (Archive)](./Docs/Migration_Archive/BASELINE.md)
- [Legacy Capability Inventory (Archive)](./Docs/Migration_Archive/LEGACY_CAPABILITY_INVENTORY.md)

---

## 9. Troubleshooting & Common Pitfalls

### 1. Database & Offline Operation
* **Local SQLite is the Source of Truth:** RigMD automatically creates and upgrades its local SQLite database on launch at `%LocalAppData%\RigMD\rigmd.db` with WAL (Write-Ahead Logging) enabled.
* **No `DATABASE_URL` Required:** RigMD operates 100% offline out-of-the-box. If a Supabase `DATABASE_URL` is provided, optional cloud sync occurs in the background without blocking local diagnoses.

### 2. Port 5273 Already in Use
* If an orphaned `RigMD.Api.exe` process is still running from a previous debugger session, kill it in PowerShell:
  ```powershell
  Stop-Process -Name "RigMD.Api" -Force
  ```
  *(In release builds, RigMD uses a Windows Job Object and Mutex to automatically prevent stale orphan processes).*

### 3. Gemini API Key & Offline Brain
* If no Gemini API key is configured in `appsettings.json`, you can paste it directly into the in-app **Settings** page.
* If you have no internet access or no API key, RigMD automatically switches to its built-in offline diagnostic engine so scanning and remediation never fail.
