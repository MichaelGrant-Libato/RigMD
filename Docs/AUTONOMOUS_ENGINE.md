# Autonomous ReAct Engine

## Overview
RigMD's Autonomous Engine operates as a transparent, multi-turn **ReAct (`Reason + Act`)** diagnostic and remediation loop on Windows. Rather than relying on static `if/else` lookup tables or simulated dry-run strings, the engine dynamically invokes read-only Windows telemetry tools, synthesizes live hardware/OS observations, computes real pre-flight impact previews, and verifies before/after telemetry deltas after user-approved execution.

---

## End-to-End ReAct Lifecycle

```
1. User Symptom & Baseline Context
       ↓
2. ReAct Investigation Loop (Up to 4 Turns)
   ├── [THOUGHT] Agent formulates diagnostic hypothesis
   ├── [TOOL CALL] Agent invokes Tier 0 read-only WMI/OS tools
   └── [OBSERVATION] Live JSON telemetry returned to agent context
       ↓
3. Root Cause Synthesis & Tool Selection
   └── Agent cites live telemetry metrics and selects a Tier 1 or Tier 2 remediation tool
       ↓
4. Non-Destructive Dry-Run Impact Preview (PreviewImpactAsync)
   └── Measures actual file counts, reclaimable bytes, running process locks, and Admin elevation
       ↓
5. Explicit User Consent Gate (AwaitingConsent)
   └── User reviews ReAct trace + dry-run metrics and approves execution
       ↓
6. Pre-Execution Baseline -> Real OS Execution -> Post-Execution Verification
   ├── Paired Tier 0 tool captures Before Snapshot JSON
   ├── Tier 1 / Tier 2 remediation tool executes real Windows fix
   └── Paired Tier 0 tool captures After Snapshot JSON & computes delta proof
```

---

## 1. Dual-Mode Reasoning Client (`GeminiReActLlmClient`)
Implemented in `RigMD.Infrastructure/Ai/GeminiReActLlmClient.cs` behind `IReActLlmClient`:
- **Google Gemini Free-Tier Function Calling (`Gemini (gemini-2.5-flash)`)**: When `Gemini:ApiKey` (in `appsettings.json`) or `GEMINI_API_KEY` (environment variable) is provided, the engine sends all 14 `AgentToolFunctionDeclaration` JSON schemas to the Gemini REST API and executes multi-turn `functionCall` / `functionResponse` cycles until the model invokes `submit_diagnosis_and_remediation_plan`.
- **Zero-Cost Local Tool-Calling Fallback (`RigMD Local Tool-Calling ReAct Engine`)**: When running offline or without an API key (`$0.00` cost), the client still drives a multi-turn `Thought -> Tool Call -> Observation` loop:
  - **Turn 0**: Selects and invokes 2–3 targeted `Tier0_ReadOnly` tools based on symptom context.
  - **Turn 1+**: Parses the live JSON observations returned by those tools (actual `%TEMP%` MB, Chrome/Edge cache MB, `SoftwareDistribution\Download` MB, RAM usage %, browser memory MB, live DNS lookup latency ms) and synthesizes a data-driven root-cause analysis and tool proposal.

---

## 2. Real Dry-Run Impact Previews (`PreviewImpactAsync`)
Every `IRigMdAgentTool` implements `PreviewImpactAsync(JsonElement arguments)` to perform a **real, non-destructive pre-flight check** before the user approves execution:
- **`clear_temp_files`**: Enumerates `%TEMP%` (and optionally `C:\Windows\Temp`) to count accessible files and sum exact bytes to be reclaimed.
- **`clear_browser_cache`**: Measures Chrome, Edge, and Firefox cache directories in bytes and checks `Process.GetProcessesByName` to warn if a browser is currently running and locking cache files.
- **`terminate_processes`**: Validates target process names against the protected Windows/RigMD denylist, enumerates matching live PIDs, and sums exact working-set RAM MB that will be freed.
- **`clear_windows_update_cache` & `run_system_file_checker`**: Checks whether the current RigMD process is running with Windows Administrator elevation (`WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`) and measures `C:\Windows\SoftwareDistribution\Download`.

---

## 3. Paired Before/After Telemetry Verification
When the user approves execution (`POST /api/autonomy/execute` with `userConsentProvided: true`), `AutonomousOrchestrator` automatically pairs the remediation tool with its corresponding `Tier0_ReadOnly` verification tool:

| Executed Remediation Tool | Paired `Tier0_ReadOnly` Verification Tool |
|---|---|
| `clear_temp_files`, `clear_browser_cache`, `clear_windows_update_cache` | `inspect_storage_health` |
| `terminate_processes`, `restart_windows_explorer` | `inspect_memory_and_processes` |
| `flush_dns_cache` | `inspect_network_connectivity` |
| `run_system_file_checker` | `inspect_cpu_and_thermals` |

The orchestrator runs the paired `Tier0_ReadOnly` tool immediately **before** and **after** executing the remediation tool, storing both raw JSON snapshots and the before/after `ExecutionProof` deltas in `PostExecutionVerificationReport`.

---

## 4. Real-Time SignalR Trace Streaming
`AutonomyController` streams both structured `ReceiveReActStep` payloads (`ReActTraceStep`) and `ReceiveProgress` terminal lines over `/hubs/remediation`. The React `AutonomyRemediationPanel` renders each step in real time with expandable **View Raw Telemetry JSON** inspectors.
