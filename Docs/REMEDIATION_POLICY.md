# Remediation & Tool Safety Policy

## Overview
This document defines the safety boundaries of the RigMD ReAct Agent (`IRigMdAgentToolRegistry` and `AutonomousOrchestrator`), governing which tools the agent may execute automatically during diagnostic reasoning versus which tools require a non-destructive dry-run preview and explicit user consent.

---

## 1. Three-Tier Tool Safety Classification (`ToolSafetyTier`)

### `Tier0_ReadOnly` — Diagnostic Inspection Tools (Auto-Executed in ReAct Loop)
Pure read-only hardware, sensor, process, network, storage, and Event Log inspection tools that never mutate OS state.
- **Registered Tools**:
  1. `inspect_cpu_and_thermals` — CPU load, clock speeds, throttling flags, and package temperature.
  2. `inspect_memory_and_processes` — Physical/committed RAM telemetry and top running process groups (`topN`, `sortBy`).
  3. `inspect_storage_health` — Drive SMART status, volume free space, and byte measurements of `%TEMP%`, `C:\Windows\Temp`, `SoftwareDistribution\Download`, and browser caches.
  4. `inspect_gpu_and_displays` — GPU driver version/date, dedicated/shared VRAM, core temperature, and connected displays.
  5. `inspect_network_connectivity` — Active adapter status, gateway/DNS config, and live timed DNS lookup + ICMP ping probe.
  6. `InspectBatteryAndPowerTool` (`inspect_battery_and_power`) — Chassis type, active Windows power plan, and battery status/health.
  7. `query_windows_event_logs` — Recent Critical/Error/Warning events from Windows `System` or `Application` logs via `wevtutil.exe`.
- **Execution Policy**: The ReAct orchestrator may invoke `Tier0_ReadOnly` tools autonomously during reasoning turns and stream their JSON observations directly to the UI.

---

### `Tier1_SafeReversible` — Low-Risk OS Maintenance Tools (Dry-Run + User Consent Required)
Low-risk, standard-user-safe Windows maintenance actions that do not touch personal documents, credentials, or system configuration.
- **Registered Tools**:
  1. `clear_temp_files` — Deletes unlocked temporary files in `%TEMP%` (and optionally `C:\Windows\Temp`), skipping any files actively locked by running processes.
  2. `flush_dns_cache` — Runs `ipconfig /flushdns` to purge stale DNS resolver records without dropping active connections.
  3. `restart_windows_explorer` — Restarts the `explorer.exe` shell process and verifies the new shell PID is running.
- **Execution Policy**:
  - **Safety Gate Interception**: Even if an LLM attempts to call a `Tier1_SafeReversible` tool directly during a reasoning turn, `AutonomousOrchestrator` intercepts the call before execution, runs `PreviewImpactAsync`, and pauses in `AwaitingConsent`.
  - Requires explicit user checkbox confirmation in the UI before execution.

---

### `Tier2_DestructiveOrAdmin` — Higher-Impact or Administrator Tools (Strict Guardrails + User Consent Required)
Actions that close running user applications, clear application caches, stop/start Windows services, or require Administrator elevation.
- **Registered Tools**:
  1. `terminate_processes` — Gracefully closes (`CloseMainWindow`) and force-terminates (`Kill(entireProcessTree: true)` after 2s) specified user processes.
     - **Hard Denylist Guardrail**: Strictly blocks termination of protected Windows OS, security/Defender, and RigMD processes (`lsass`, `csrss`, `svchost`, `smss`, `wininit`, `winlogon`, `dwm`, `explorer`, `msmpeng`, `mssense`, `rigmd.api`, `dotnet`, etc.) via `TerminateProcessesAction.IsProtectedProcess`.
  2. `clear_browser_cache` — Clears Chrome, Edge, and Firefox disk cache folders (`Cache` / `cache2`) while leaving passwords, cookies, bookmarks, and history untouched. Warns during dry-run if target browsers are currently running.
  3. `clear_windows_update_cache` — Stops `wuauserv`, purges `C:\Windows\SoftwareDistribution\Download`, and restarts `wuauserv`. Checks Administrator elevation during dry-run.
  4. `run_system_file_checker` — Executes `sfc.exe /scannow` with UTF-16LE output decoding. Enforces Administrator elevation (`CanExecute = false` if not elevated).
- **Execution Policy**:
  - Strictly intercepted by the orchestrator safety gate during reasoning.
  - Requires non-destructive `PreviewImpactAsync` dry-run preview, privilege verification, and explicit user consent.

---

## 2. Prohibition of Arbitrary Command Execution
RigMD never exposes a generic shell, PowerShell, command-line, or registry-editing tool to the LLM. Only the explicitly compiled, strongly-typed `IRigMdAgentTool` implementations registered in `Program.cs` can be invoked.

## 3. Auditability & Telemetry Proof
Every tool execution records:
- Full multi-turn `ReActTraceStep` history (`Thought`, `ToolCall`, `Observation`, `DryRunPreview`, `Execution`, `Verification`).
- Paired `Tier0_ReadOnly` before and after JSON snapshots (`PostExecutionVerificationReport`).
- Concrete `ExecutionProof` items persisted to the local SQLite `RemediationRun` history.
