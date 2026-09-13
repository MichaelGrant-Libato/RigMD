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
| Desktop Shell | WPF + WebView2 |
| Hardware Telemetry | Native Windows WMI / System.Management |
| Diagnostic Engine | Deterministic C# Domain layer |
| Persistence | SQLite (Local Source of Truth) & Supabase PostgreSQL (Optional Sync) |
| Remediation | Controlled Autonomous Remediation Engine |
| Real-Time Streaming | SignalR WebSocket Hub |
| AI Integration | Constrained Google Gemini Explanations (with offline fallback) |
| Remote Agent | RigMD Agent (Windows Service) |

---

## 3. Repository Structure

```
RigMD/
│
├── backend/
│   └── Legacy Python/FastAPI implementation
│       retained for migration reference
│
├── backend-dotnet/
│   │
│   ├── RigMD.slnx
│   │
│   ├── RigMD.Api/
│   │   ├── Controllers/
│   │   │   ├── AgentController.cs
│   │   │   ├── AutonomyController.cs
│   │   │   ├── DashboardController.cs
│   │   │   ├── DatabaseController.cs
│   │   │   ├── DiagnosticController.cs
│   │   │   ├── HardwareController.cs
│   │   │   ├── ProfilesController.cs
│   │   │   ├── RecurringController.cs
│   │   │   ├── RemediationController.cs
│   │   │   └── WarningSignsController.cs
│   │   ├── Hubs/
│   │   │   └── RemediationHub.cs
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
│   │   └── Services/
│   │       ├── Autonomy/
│   │       │   ├── AutonomousOrchestrator.cs
│   │       │   ├── DryRunRemediationExecutor.cs
│   │       │   ├── PivotEngine.cs
│   │       │   ├── RemediationPlanner.cs
│   │       │   ├── RemediationRegistry.cs
│   │       │   └── SafetyPolicy.cs
│   │       ├── AutomaticDiagnosisService.cs
│   │       ├── DiagnosticEngineService.cs
│   │       ├── RecurringPatternService.cs
│   │       ├── ResolutionService.cs
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
│   │   │   └── OfflineAiExplainer.cs
│   │   ├── Persistence/
│   │   │   ├── AgentRepository.cs
│   │   │   ├── DatabaseSyncService.cs
│   │   │   ├── DiagnosticSessionRepository.cs
│   │   │   ├── LocalDatabaseSchemaUpgradeService.cs
│   │   │   ├── RemediationRepository.cs
│   │   │   └── RigMdDbContext.cs
│   │   ├── Remediation/
│   │   │   ├── Actions/
│   │   │   │   ├── ClearBrowserCacheAction.cs
│   │   │   │   ├── ClearTempFilesAction.cs
│   │   │   │   ├── ClearWindowsUpdateCacheAction.cs
│   │   │   │   ├── FlushDnsAction.cs
│   │   │   │   ├── RunDiskCleanupAction.cs
│   │   │   │   └── RunSfcScanAction.cs
│   │   │   ├── RollbackManager.cs
│   │   │   ├── VerificationService.cs
│   │   │   └── WindowsRemediationExecutor.cs
│   │   └── Windows/
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
│   ├── RigMD.Agent/
│   │   ├── Tools/
│   │   ├── Services/
│   │   ├── Worker.cs
│   │   └── Program.cs
│   │
│   ├── RigMD.Desktop/
│   │   ├── App.xaml.cs
│   │   ├── MainWindow.xaml.cs
│   │   └── wwwroot/
│   │
│   └── RigMD.Tests/
│       ├── Api/
│       ├── Application/
│       ├── Domain/
│       └── Infrastructure/
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
│       │       ├── DiagnosticResultView.tsx
│       │       ├── DiagnosticHistoryView.tsx
│       │       ├── DiagnosticSessionDetailView.tsx
│       │       ├── RecurringPatternsView.tsx
│       │       ├── WarningSignsView.tsx
│       │       ├── HardwareDashboard.tsx
│       │       ├── SystemProfileView.tsx
│       │       ├── HelpScopeView.tsx
│       │       ├── ShareReportView.tsx
│       │       └── ...
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
| Clear User Temp Files | Removes temporary files from user TEMP folder | Tier 1 |
| Clear Browser Cache | Clears browser cache data | Tier 1 |
| Clear Windows Update Cache | Clears Windows Update download cache | Tier 1 |
| Flush DNS Cache | Resets the DNS resolver cache | Tier 1 |
| Run Disk Cleanup | Invokes Windows Disk Cleanup utility | Tier 1 |
| Run SFC Scan | Runs Windows System File Checker | Tier 1 |

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

RigMD collects live hardware telemetry via Windows Management Instrumentation (WMI). Each provider implements a contract interface in `RigMD.Application.Contracts.Providers` and is implemented in `RigMD.Infrastructure.Windows`.

| Provider | WMI Class / Source | Data Collected |
|---|---|---|
| `WmiCpuProvider` | `Win32_Processor`, Performance Counters | Name, usage %, cores, threads, frequency, thermal throttling |
| `WmiGpuProvider` | `Win32_VideoController` | Name, driver version, type (Dedicated/Integrated), VRAM |
| `WmiMemoryProvider` | `Win32_OperatingSystem` | Total/used GB, usage % |
| `WmiStorageProvider` | `Win32_DiskDrive`, `Win32_LogicalDisk` | Drive models, types (NVMe/SATA/HDD), SMART status, volumes |
| `WmiMotherboardProvider` | `Win32_BaseBoard` | Chipset/product name |
| `WmiOperatingSystemProvider` | `Win32_OperatingSystem` | OS version, device name, system age |
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

### Start the System (Local Desktop Mode)

The latest architecture bundles the frontend inside the backend, bypassing the need for a separate Agent service or a cloud database for local diagnosis.

From the repository root:

```bash
cd backend-dotnet
dotnet run --project RigMD.Desktop
```

This will automatically launch the WPF desktop wrapper and serve the React UI.

*Note: If you still need to run the React app separately for frontend development, you can use `npm run dev` in the `frontend` folder.*

---

## 7. Build and Test

### Backend

```bash
cd backend-dotnet
dotnet build
dotnet test
```

### Frontend

```bash
cd frontend
npm run build
```

Current automated test coverage includes:

- Diagnostic engine rule classification
- Automatic diagnosis service (component/scenario/full modes)
- Autonomous orchestration lifecycle
- Remediation planner and registry
- Safety policy enforcement
- Warning sign normalization
- Verification service behavior
- Diagnostic session repository persistence
- Remediation repository persistence
- Autonomy controller integration

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

If you are setting up the C# environment for the first time or testing the Agent locally, watch out for these common issues:

### 1. `DATABASE_URL is not configured` (API Crash)
**The Problem:** The `AgentRepository` currently still relies on Supabase (PostgreSQL) instead of the local SQLite database. If your `.env` or `appsettings.Development.json` is missing the `DATABASE_URL`, the API will crash on startup or when the agent heartbeats.
**The Fix:** Add `DATABASE_URL` to `backend-dotnet/RigMD.Api/appsettings.Development.json`. 

### 2. API Hangs / Error 500 `TimeoutException` (Npgsql & PgBouncer)
**The Problem:** If your `DATABASE_URL` uses port `6543`, you are connecting to Supabase's `PgBouncer` connection pooler. The C# `.NET Npgsql` driver uses prepared statements by default, which are incompatible with PgBouncer in Transaction Mode, causing queries to hang and time out.
**The Fix:** Change the port in your `DATABASE_URL` from `6543` to `5432` to connect directly to the Postgres instance.

### 3. Frontend Shows Another PC (e.g., "MIKMIKYULAPPY")
**The Problem:** You copied `VITE_AGENT_ID` from a co-developer's `.env` file instead of using your own. The API correctly queried Supabase for that ID, returning your co-worker's PC hardware.
**The Fix:** 
1. Open `C:\ProgramData\RigMD\agent.json` on your local machine.
2. Copy the `AgentId`.
3. Paste it into `frontend/.env.local` as `VITE_AGENT_ID=your-local-guid`.
4. Restart the Vite dev server.

### 4. Agent "Offline" / Not Running as a Service
**The Problem:** The RigMD Agent may not be installed natively as a Windows Service on your development machine yet. 
**PowerShell Gotcha:** If you try to check the service status in PowerShell using `sc qc RigMDAgent`, it will **not** query the service. `sc` in PowerShell is an alias for `Set-Content`! You will accidentally create a text file named `qc` with the text "RigMDAgent".
**The Fix:** Use `sc.exe query RigMDAgent` in PowerShell, or just manually run the agent for testing: `dotnet run --project RigMD.Agent`.