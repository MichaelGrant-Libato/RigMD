# Remediation & Tool Safety Policy

## Current implementation — 0.2.18

The shared `AgentToolRegistration.AddRigMdAgentTools` list registers **19 tools: 11 read-only, 2 Tier 1 and 6 Tier 2**. `Program.cs` and the registry contract test use that shared list. Classification never bypasses explicit user approval for system changes; a Tier 1 label is not a universal undo guarantee. The separately approved selected-app, uninstall, personal-file recycling and Recycle Bin workflows are additional API routes rather than agent registry entries.

Heat, battery and elevated-CPU findings provide investigation guidance. They do not authorize an unnamed app-close request. App closing requires an independently supported memory workflow. Crash records and unavailable readings also remain guidance-only; explicit per-finding storage review retains its own verified scope and warning checks.

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
  8. `query_startup_apps` — Read startup registrations without disabling them.
  9. `inspect_full_device_profile` — Read full device inventory and supported reported device-error evidence.
  10. `query_past_checks_and_remediations` — Read saved diagnostic and action history.
  11. `query_recurring_problems_and_warnings` — Read historical patterns and recorded warnings.
- **Execution Policy**: The ReAct orchestrator may invoke `Tier0_ReadOnly` tools autonomously during reasoning turns and stream their JSON observations directly to the UI.

---

### `Tier1_SafeReversible` — Low-Risk OS Maintenance Tools (Dry-Run + User Consent Required)
Low-risk, standard-user-safe Windows maintenance actions that do not touch personal documents, credentials, or system configuration.
- **Registered Tools**:
  1. `flush_dns_cache` — Runs `ipconfig /flushdns` to purge stale DNS resolver records without dropping active connections.
  2. `restart_windows_explorer` — Restarts only the approved desktop-shell process in the current normal user/session, then verifies a different process identity owning the shell window and confirms the original process is gone. Elevated RigMD is blocked from this operation; reopen it normally. Separate Explorer processes and other Windows sessions are not targeted.
- **Execution Policy**:
  - **Safety Gate Interception**: Even if an LLM attempts to call a `Tier1_SafeReversible` tool directly during a reasoning turn, `AutonomousOrchestrator` intercepts the call before execution, runs `PreviewImpactAsync`, and pauses in `AwaitingConsent`.
  - Requires explicit user checkbox confirmation in the UI before execution.

---

### `Tier2_DestructiveOrAdmin` — Higher-Impact or Administrator Tools (Strict Guardrails + User Consent Required)
Actions that permanently delete temporary files, close running user applications, clear application caches, stop/start Windows services, or require Administrator elevation.
- **Registered Tools**:
  1. `terminate_processes` - Legacy tool identifier for reviewed normal app-close requests (`CloseMainWindow`). No force-stop fallback is permitted. Background processes can remain running; requesting closure is not confirmed exit or proof of memory relief. The user-selected `close_selected_app` route follows the same normal-close policy and binds approval to the exact selected apps.
     - **Hard Denylist Guardrail**: Strictly blocks termination of protected Windows OS, security/Defender, and RigMD processes (`lsass`, `csrss`, `svchost`, `smss`, `wininit`, `winlogon`, `dwm`, `explorer`, `msmpeng`, `mssense`, `rigmd.api`, `dotnet`, etc.) via `TerminateProcessesAction.IsProtectedProcess`.
  2. `clear_browser_cache` — Clears Chrome, Edge, and Firefox disk cache folders (`Cache` / `cache2`) while leaving passwords, cookies, bookmarks, and history untouched. Warns during dry-run if target browsers are currently running.
  3. `clear_windows_update_cache` — Requires an administrator-only, bounded, exact-file snapshot for `SoftwareDistribution\Download` and binds the initial stable `wuauserv` state. No deletion begins unless stopped state is verified. Restoration runs even after stop cancellation/failure, and the original service state must be verified afterward. Initially stopped services are left stopped. A service-state change since preview blocks execution. Partial file removal or unverified service recovery is not reported as full success. Legacy execution without the approved snapshot is blocked.
  4. `run_system_file_checker` — Executes `sfc.exe /scannow` with UTF-16LE output decoding. Enforces Administrator elevation (`CanExecute = false` if not elevated).
  5. `clear_temp_files` — Permanently deletes only files captured in a fresh approved snapshot. This bypasses the Recycle Bin and cannot be undone by RigMD. The server chooses the user TEMP folder and optional Windows Temp folder; request arguments cannot supply a path. `includeWindowsTemp` defaults to false and requires administrator access when true. `minAgeMinutes` is an integer from 0 to 525600, default 0; creation and last-write times must both be at or before the preview-time age cutoff. Unknown, duplicated or incorrectly typed arguments are rejected.
  6. `rescan_plug_and_play_devices` — Runs the fixed Windows device-rescan operation after administrator verification, current evidence/scope checks, preview and explicit approval. It does not install drivers or repair physical damage.
- **Execution Policy**:
  - Strictly intercepted by the orchestrator safety gate during reasoning.
  - Requires non-destructive `PreviewImpactAsync` dry-run preview, privilege verification, and explicit user consent.

Temporary-file previews keep the file identities and timestamps on the server, attached to the existing session/tool/argument-bound, single-use approval. The snapshot expires after five minutes and is never rebuilt to expand an approved execution. Scanning is bounded to 3 seconds, 10,000 entries and 500 eligible files; partial scans are disclosed. Newly created files and empty directories are left alone. Before deletion starts, available files are opened without allowing concurrent writes or renames and their identity, size and timestamps are rechecked. Changed, missing, replaced, locked or unverifiable candidates are skipped; they cannot be reconsidered without a fresh preview and approval. Other unchanged approved candidates can proceed. Cleanup-root identity changes stop the whole action. Root and ancestor directory handles reject junctions, symlinks and cloud-recall paths; linked, offline, read-only, system and multiply-linked files are excluded. Cancellation stops further deletions and reports any completed work. Receipts list previewed, deleted and not-removed counts separately. An unsuccessful action remains an execution failure; it does not itself verify the original condition. Logical deleted bytes do not guarantee recovered drive space. Legacy dispatch without the approved snapshot fails closed.

---

## 2. Prohibition of Arbitrary Command Execution
RigMD never exposes a generic shell, PowerShell, command-line, or registry-editing tool to the LLM. Only the explicitly compiled, strongly-typed `IRigMdAgentTool` implementations registered in `Program.cs` can be invoked.

### Browser-cache approval scope

Browser-cache deletion is Tier 2 permanent deletion. The backend accepts only exact, nonduplicated `chrome`, `edge`, and `firefox` identifiers; an omitted selection means all three, while empty, unknown, duplicate, or incorrectly typed arguments are rejected. Coverage is limited to Chrome/Edge Default-profile Cache folders and Firefox local-profile cache2 folders. The app does not infer which other browsers or profiles are safe to clean.

Preview captures a bounded server-only file-identity snapshot for the selected browsers. The existing single-use approval store retains that context across requests; execution requires the same browser set and Windows account folder and never rescans to enlarge the approved targets. Temporary-file and browser-cache deletion share the same parent-directory pinning and same-handle file-identity checks. Changed, replaced, locked, linked, or unverifiable files are skipped, new files and profiles are left alone, and a replaced cleanup root stops the action. No browser is closed, no empty directories are removed, and legacy dispatch without a matching approved snapshot fails closed. Receipts report deleted logical bytes separately from any later drive-space measurement.

## 3. Auditability & Telemetry Proof
Executed generic tools and selected-app requests save action attempts and available proof to local SQLite history. Selected-app requests save each app's observed outcome; original-condition verification remains unknown until the separate follow-up check. Generic tools may also return paired inspection snapshots. A returned reasoning trace is not a guarantee that every trace step is durably retained.

If history saving fails after execution, the response warns that the action already ran; users must not repeat it merely to save history. A crash-history query, a high memory reading, a missing sensor and a completed system-file scan are separate facts. None alone proves a crash cause or successful repair. Rechecking remains available after a successful observation and is disabled only while a check is running.

## Approval-bound Explorer restart

The approval binds the current user's session, SID, actual desktop-shell process ID, start time and Windows executable path. A changed shell/session or expired snapshot requires a new preview. The implementation never enumerates all processes named Explorer for termination. If Windows automatically supplies a replacement shell, no duplicate launch is made. Recovery can restore a missing shell after a canceled stop, but cancellation or failed stopping is not relabeled as successful execution. The receipt describes a replacement shell-window observation, not proof of display/device repair.

## Windows Update file and service recovery

Update-download cleanup shares the same parent-chain pinning, file identity and same-handle deletion protection as temporary/browser cleanup. The snapshot is bounded to the existing 3-second/10,000-entry/500-file policy and expires after five minutes. New files remain untouched; changed/locked candidates are skipped. No empty directory removal or arbitrary client path/service name is supported. Service transitions use the fixed Windows Service Control Manager API identity, not command-line lookup. Stable running/stopped states are required. Recovery attempts have a bounded independent timeout even when the original request is canceled. Partial deletion and recovery failure remain unsuccessful, with available deletion/service proof retained. A successful subset cleanup does not claim that the entire download cache was cleared or that an update/storage problem was repaired.

Read-only native API checks and fake-service/process regression tests are separate from installed acceptance. No real update-service stop or Explorer restart is run by the automated suite.
