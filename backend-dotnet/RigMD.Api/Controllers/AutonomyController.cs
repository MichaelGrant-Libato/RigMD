using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Hubs;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AutonomyController : ControllerBase
{
    private static readonly HashSet<string> ProtectedProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "aggregatorhost",
            "applicationframehost",
            "audiodg",
            "backgroundtaskhost",
            "conhost",
            "ctfmon",
            "csrss",
            "dllhost",
            "dwm",
            "explorer",
            "fontdrvhost",
            "lsass",
            "memory compression",
            "memorycompression",
            "mpcmdrun",
            "mpcopyaccelerator",
            "mpdefendercoreservice",
            "mpsigstub",
            "msmpeng",
            "mssense",
            "msedgewebview2",
            "nissrv",
            "registry",
            "runtimebroker",
            "searchhost",
            "searchindexer",
            "secure system",
            "securesystem",
            "securityhealthhost",
            "securityhealthservice",
            "securityhealthsystray",
            "sense",
            "service host",
            "servicehost",
            "services",
            "shellexperiencehost",
            "sihost",
            "sgrmbroker",
            "smartscreen",
            "smss",
            "spoolsv",
            "startmenuexperiencehost",
            "svchost",
            "system",
            "system idle process",
            "taskmgr",
            "taskhostw",
            "textinputhost",
            "unsecapp",
            "widgets",
            "widgetservice",
            "wininit",
            "winlogon",
            "wmiprvse",
            "wudfhost",
            "rigmd",
            "rigmd.api",
            "rigmd.agent",
            "rigmd.desktop",
            "dotnet"
        };

    private readonly IAutonomousOrchestrator _orchestrator;
    private readonly IWindowsSystemProfileService _profileService;
    private readonly IDiagnosticSessionRepository _sessionRepository;
    private readonly IRemediationRepository _remediationRepository;
    private readonly IHubContext<RemediationHub> _hubContext;
    private readonly ILogger<AutonomyController> _logger;
    private readonly IRigMdAgentToolRegistry? _toolRegistry;

    public AutonomyController(
        IAutonomousOrchestrator orchestrator,
        IWindowsSystemProfileService profileService,
        IDiagnosticSessionRepository sessionRepository,
        IRemediationRepository remediationRepository,
        IHubContext<RemediationHub> hubContext,
        ILogger<AutonomyController> logger,
        IRigMdAgentToolRegistry? toolRegistry = null)
    {
        _orchestrator = orchestrator;
        _profileService = profileService;
        _sessionRepository = sessionRepository;
        _remediationRepository = remediationRepository;
        _hubContext = hubContext;
        _logger = logger;
        _toolRegistry = toolRegistry;
    }

    public class PreviewRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string DiagnosedCategory { get; set; } = string.Empty;
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
        {
            return BadRequest(new
            {
                message = "A valid diagnostic session ID is required."
            });
        }

        var diagnostic =
            await _sessionRepository.GetDiagnosticOutputAsync(sessionId);

        if (diagnostic == null)
        {
            return NotFound(new
            {
                message = "Diagnostic session was not found."
            });
        }

        var hardware = _profileService.GetLiveSystemProfile();

        var result =
            await _orchestrator.RunDryRunCycleAsync(
                diagnostic,
                hardware,
                progressReporter: msg =>
                {
                    _ = _hubContext.Clients.All.SendAsync("ReceiveProgress", msg);
                },
                stepReporter: step =>
                {
                    _ = _hubContext.Clients.All.SendAsync("ReceiveReActStep", step);
                });

        if (result.Plan != null)
        {
            result.Plan.SessionId = sessionId.ToString();
        }

        return Ok(result);
    }

    [HttpGet("memory-apps")]
    public IActionResult GetMemoryApps()
    {
        var hardware = _profileService.GetLiveSystemProfile();

        var apps =
            hardware.ProcessInsights.TopMemoryApps
                .Where(app =>
                    app.MemoryMb >= 100 &&
                    IsEligibleUserApp(app.Name))
                .Select(app => new
                {
                    id = app.Name,
                    name = app.Name,
                    displayName = ToDisplayName(app.Name),
                    appKind = GetAppKind(app.Name),
                    detail = GetAppDetail(app.Name),
                    closeWarning = GetCloseWarning(app.Name),
                    processCount = app.ProcessCount,
                    memoryMb = app.MemoryMb
                })
                .Take(8)
                .ToList();

        return Ok(new
        {
            apps
        });
    }

    public class CloseSelectedAppRequest
    {
        public string ProcessName { get; set; } = string.Empty;
        public List<string> ProcessNames { get; set; } = new();
        public bool Confirmed { get; set; }
    }

    [HttpPost("close-selected-app")]
    public IActionResult CloseSelectedApp(
        [FromBody] CloseSelectedAppRequest request)
    {
        if (!request.Confirmed)
        {
            return BadRequest(new
            {
                message = "Confirm before closing the selected app."
            });
        }

        var processNames =
            request.ProcessNames.Count > 0
                ? request.ProcessNames
                : new List<string> { request.ProcessName };

        var selectedProcessNames =
            processNames
                .Select(NormalizeProcessName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

        if (selectedProcessNames.Count == 0)
        {
            return BadRequest(new
            {
                message = "Choose at least one app before continuing."
            });
        }

        var blockedProcess =
            selectedProcessNames.FirstOrDefault(
                name => !IsEligibleUserApp(name));

        if (blockedProcess != null)
        {
            return BadRequest(new
            {
                message = $"RigMD will not close {ToDisplayName(blockedProcess)} because it is a protected Windows, background, or RigMD process."
            });
        }

        var beforeHardware =
            _profileService.GetLiveSystemProfile();

        var selectedApps =
            beforeHardware.ProcessInsights.TopMemoryApps
                .Where(app =>
                    selectedProcessNames.Contains(
                        app.Name,
                        StringComparer.OrdinalIgnoreCase))
                .ToList();

        if (selectedApps.Count == 0)
        {
            return BadRequest(new
            {
                message = "Those apps are no longer showing as memory-heavy apps. Refresh and choose again."
            });
        }

        if (selectedApps.Count != selectedProcessNames.Count)
        {
            return BadRequest(new
            {
                message = "One selected app is no longer showing as memory-heavy. Refresh and choose again."
            });
        }

        var outcomes =
            selectedApps
                .Select(CloseApp)
                .ToList();

        Thread.Sleep(TimeSpan.FromSeconds(2));

        var afterHardware =
            _profileService.GetLiveSystemProfile();

        foreach (var outcome in outcomes)
        {
            var afterApp =
                afterHardware.ProcessInsights.TopMemoryApps.FirstOrDefault(
                    app =>
                        app.Name.Equals(
                            outcome.ProcessName,
                            StringComparison.OrdinalIgnoreCase));

            outcome.AfterMemoryMb =
                afterApp?.MemoryMb ?? 0;
        }

        var success =
            outcomes.Any(outcome => outcome.Success);

        var displayNames =
            outcomes
                .Select(outcome => outcome.DisplayName)
                .ToList();

        var proof =
            outcomes
                .Select(outcome => new
                {
                    label = outcome.DisplayName,
                    status = outcome.Success ? "Closed or reduced" : "Could not confirm",
                    meaning = $"RigMD tried to close {outcome.DisplayName}. {GetCloseWarning(outcome.ProcessName)}",
                    before = $"{outcome.BeforeMemoryMb:0.##} MB across {outcome.BeforeProcessCount} process(es)",
                    after = $"{outcome.AfterMemoryMb:0.##} MB; {outcome.ClosedWindows} normal close request(s), {outcome.ForceClosedProcesses} force close(s), {outcome.FailedProcesses} blocked attempt(s)"
                })
                .ToArray();

        return Ok(new
        {
            plan = new
            {
                plannedActions = new[]
                {
                    new
                    {
                        id = "close_selected_app",
                        name = displayNames.Count == 1
                            ? $"Close {displayNames[0]}"
                            : $"Close {displayNames.Count} selected apps",
                        description = "Close only the apps selected by the user.",
                        category = "Troubleshoot",
                        riskLevel = "User-confirmed",
                        isReversible = false,
                        requiresUserConfirmation = true
                    }
                }
            },

            safety = new
            {
                isApproved = true,
                requiresUserConfirmation = true,
                warnings = new[]
                {
                    "Selected apps may close open windows, tabs, downloads, meetings, terminals, or unsaved work."
                }
            },

            execution = new
            {
                success,
                summary = success
                    ? $"RigMD closed or reduced {string.Join(", ", displayNames)}. It will now recheck whether memory pressure improved."
                    : $"RigMD tried to close {string.Join(", ", displayNames)}, but Windows did not confirm that they closed.",
                proof
            },

            attempts = new[]
            {
                new
                {
                    action = new
                    {
                        id = "close_selected_app",
                        name = displayNames.Count == 1
                            ? $"Close {displayNames[0]}"
                            : $"Close {displayNames.Count} selected apps",
                        riskLevel = "User-confirmed",
                        isReversible = false,
                        requiresUserConfirmation = true
                    },
                    state = success ? "Completed" : "Failed",
                    notes = success
                        ? "Closed or reduced the user-selected apps."
                        : "Could not confirm that the selected apps closed."
                }
            },

            trace = $"[LOCAL] close_selected_app requested for {string.Join(", ", selectedProcessNames)}."
        });
    }

    public class ExecuteRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public string DiagnosedCategory { get; set; } = string.Empty;
        public bool UserConsentProvided { get; set; }
        public string? ToolName { get; set; }
        public string? ToolArgumentsJson { get; set; }
    }

    /// <summary>
    /// Runs a REAL remediation cycle:
    /// Pre-Execution Verification Snapshot -> Tool Execution -> Post-Execution Verification.
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] ExecuteRequest request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
        {
            return BadRequest(new
            {
                message = "A valid diagnostic session ID is required."
            });
        }

        var diagnostic =
            await _sessionRepository.GetDiagnosticOutputAsync(sessionId);

        if (diagnostic == null)
        {
            return NotFound(new
            {
                message = "Diagnostic session was not found."
            });
        }

        // Real hardware is used for safety evaluation and as the
        // persisted pre-execution system snapshot.
        var hardware = _profileService.GetLiveSystemProfile();

        var result =
            await _orchestrator.RunExecutionCycleAsync(
                diagnostic,
                hardware,
                request.UserConsentProvided,
                progressReporter: msg =>
                {
                    _ = _hubContext.Clients.All.SendAsync("ReceiveProgress", msg);
                },
                requestedToolName: request.ToolName,
                requestedArgumentsJson: request.ToolArgumentsJson,
                stepReporter: step =>
                {
                    _ = _hubContext.Clients.All.SendAsync("ReceiveReActStep", step);
                });

        if (result.Plan != null)
        {
            result.Plan.SessionId = sessionId.ToString();
        }

        /*
         * Persist only when at least one action actually reached the
         * execution layer.
         *
         * Safety-rejected / consent-required requests do not create
         * remediation history because no remediation was executed.
         */
        if (result.Attempts.Any(a =>
                a.Action != null &&
                a.Execution != null))
        {
            try
            {
                var run =
                    BuildRemediationRun(
                        diagnostic.Id,
                        result,
                        hardware);

                await _remediationRepository.SaveRunAsync(run);

                result.Trace +=
                    $"\n[PERSISTENCE] Remediation run saved: {run.Id}";
            }
            catch (Exception ex)
            {
                /*
                 * Do not convert this to a failed execution response.
                 *
                 * The system-changing action may already have happened.
                 * Returning a generic execution failure could encourage
                 * the client to repeat the real remediation.
                 */
                _logger.LogError(
                    ex,
                    "Remediation completed but its history could not be persisted.");

                result.Trace +=
                    "\n[PERSISTENCE] WARNING: The remediation executed, " +
                    "but its history could not be saved.";
            }
        }

        return Ok(result);
    }

    private static RemediationRun BuildRemediationRun(
        Guid diagnosticOutputId,
        OrchestrationResult result,
        HardwareProfileDto hardware)
    {
        var run = new RemediationRun
        {
            DiagnosticOutputId = diagnosticOutputId,
            Status = DetermineRunStatus(result),
            CompletedAt = DateTimeOffset.UtcNow
        };

        var persistedAttempts =
            new List<(RemediationAttempt Source, ActionAttempt Entity)>();

        foreach (var sourceAttempt in result.Attempts)
        {
            if (sourceAttempt.Action == null ||
                sourceAttempt.Execution == null)
            {
                continue;
            }

            var actionAttempt = new ActionAttempt
            {
                RemediationRunId = run.Id,
                ActionCode = sourceAttempt.Action.Id,

                // This is the real system state captured before the
                // remediation cycle began.
                PreconditionState = JsonSerializer.Serialize(hardware)
            };

            if (sourceAttempt.Verification.HasValue)
            {
                var verificationStatus =
                    sourceAttempt.Verification.Value;

                actionAttempt.Verification =
                    new VerificationResult
                    {
                        ActionAttemptId = actionAttempt.Id,
                        IsSuccessful =
                            verificationStatus ==
                            VerificationStatus.Resolved,

                        ObservedState =
                            JsonSerializer.Serialize(new
                            {
                                verification =
                                    verificationStatus.ToString(),

                                executionSummary =
                                    sourceAttempt.Execution.Summary,

                                proof =
                                    sourceAttempt.Execution.Proof
                            }),

                        FailureReason =
                            verificationStatus ==
                            VerificationStatus.Resolved
                                ? null
                                : verificationStatus.ToString()
                    };
            }

            run.ActionAttempts.Add(actionAttempt);

            persistedAttempts.Add(
                (sourceAttempt, actionAttempt));
        }

        foreach (var persistedAttempt in persistedAttempts)
        {
            var sourceAttempt = persistedAttempt.Source;
            var actionAttempt = persistedAttempt.Entity;

            if (sourceAttempt.RollbackResult != null)
            {
                run.RollbackEvents.Add(
                    new RollbackEvent
                    {
                        RemediationRunId = run.Id,
                        ActionAttemptId = actionAttempt.Id,

                        WasSuccessful =
                            sourceAttempt.RollbackResult.Success,

                        RestoredState =
                            JsonSerializer.Serialize(new
                            {
                                summary =
                                    sourceAttempt.RollbackResult.Summary,

                                proof =
                                    sourceAttempt.RollbackResult.Proof
                            })
                    });
            }
        }

        /*
         * A pivoted attempt is followed by the alternative action selected
         * by the orchestrator. Persist that transition when both executed
         * attempts are available.
         */
        for (var i = 0; i < persistedAttempts.Count - 1; i++)
        {
            var current = persistedAttempts[i];
            var next = persistedAttempts[i + 1];

            if (current.Source.State !=
                RemediationAttemptState.Pivoted)
            {
                continue;
            }

            run.PivotEvents.Add(
                new PivotEvent
                {
                    RemediationRunId = run.Id,

                    FromActionCode =
                        current.Source.Action?.Id ??
                        current.Entity.ActionCode,

                    ToActionCode =
                        next.Source.Action?.Id ??
                        next.Entity.ActionCode,

                    Reason =
                        string.IsNullOrWhiteSpace(
                            current.Source.Notes)
                            ? "Orchestrator pivoted to an alternative remediation action."
                            : current.Source.Notes
                });
        }

        return run;
    }

    private static string DetermineRunStatus(
        OrchestrationResult result)
    {
        if (result.Escalated)
        {
            return "Escalated";
        }

        var lastAttempt =
            result.Attempts.LastOrDefault();

        if (lastAttempt != null)
        {
            return lastAttempt.State.ToString();
        }

        if (result.Verification.HasValue)
        {
            return result.Verification.Value.ToString();
        }

        return result.Execution?.Success == true
            ? "Completed"
            : "Failed";
    }

    private static CloseAppOutcome CloseApp(
        ProcessAppDto selectedApp)
    {
        var processName =
            NormalizeProcessName(selectedApp.Name);

        var outcome =
            new CloseAppOutcome
            {
                ProcessName = processName,
                DisplayName = ToDisplayName(processName),
                BeforeMemoryMb = selectedApp.MemoryMb,
                BeforeProcessCount = selectedApp.ProcessCount
            };

        var processes =
            Process.GetProcessesByName(processName);

        if (processes.Length == 0)
        {
            outcome.FailedProcesses = selectedApp.ProcessCount;
            return outcome;
        }

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited &&
                    process.CloseMainWindow())
                {
                    outcome.ClosedWindows++;
                }
            }
            catch
            {
                outcome.FailedProcesses++;
            }
        }

        Thread.Sleep(TimeSpan.FromSeconds(2));

        foreach (var process in processes)
        {
            try
            {
                process.Refresh();

                if (process.HasExited)
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                outcome.ForceClosedProcesses++;
            }
            catch
            {
                outcome.FailedProcesses++;
            }
        }

        return outcome;
    }

    private static string NormalizeProcessName(
        string processName)
    {
        return (processName ?? string.Empty)
            .Trim()
            .Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEligibleUserApp(
        string processName)
    {
        var normalized =
            NormalizeProcessName(processName);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (ProtectedProcessNames.Contains(normalized))
        {
            return false;
        }

        if (
            normalized.Contains("defender", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("securityhealth", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("antimalware", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.Contains("rigmd", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.Contains("webview", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsSupportedClosableUserApp(normalized);
    }

    private static string ToDisplayName(
        string processName)
    {
        processName =
            NormalizeProcessName(processName);

        if (string.IsNullOrWhiteSpace(processName))
        {
            return "Selected app";
        }

        return processName switch
        {
            "applicationframehost" => "Windows app frame",
            "chatgpt" => "ChatGPT",
            "chrome" => "Google Chrome",
            "code" => "Visual Studio Code",
            "discord" => "Discord",
            "msedge" => "Microsoft Edge",
            "firefox" => "Firefox",
            "brave" => "Brave",
            "opera" => "Opera",
            "opera_gx" => "Opera GX",
            "slack" => "Slack",
            "spotify" => "Spotify",
            "steam" => "Steam",
            "teams" => "Microsoft Teams",
            "vivaldi" => "Vivaldi",
            "zoom" => "Zoom",
            _ => char.ToUpperInvariant(processName[0]) +
                 processName[1..]
        };
    }

    private static string GetAppKind(
        string processName)
    {
        var normalized =
            NormalizeProcessName(processName);

        if (IsBrowserProcess(normalized))
        {
            return "Browser";
        }

        return normalized switch
        {
            "code" => "Code editor",
            "chatgpt" => "Desktop app",
            "discord" => "Communication app",
            "slack" => "Communication app",
            "teams" => "Meeting/chat app",
            "zoom" => "Meeting app",
            "spotify" => "Media app",
            "steam" => "Game launcher",
            _ => "App"
        };
    }

    private static string GetAppDetail(
        string processName)
    {
        var normalized =
            NormalizeProcessName(processName);

        if (IsBrowserProcess(normalized))
        {
            return "This browser may have multiple tabs, downloads, private windows, or online forms open.";
        }

        return normalized switch
        {
            "code" => "This may close editor windows, integrated terminals, unsaved files, and running local projects.",
            "chatgpt" => "This may close the ChatGPT desktop app and any active conversation or upload in that window.",
            "discord" => "This may disconnect voice calls, streams, chats, or file uploads.",
            "slack" => "This may close work messages, calls, or file uploads.",
            "teams" => "This may close meetings, calls, chats, or shared files.",
            "zoom" => "This may leave current meetings or stop active calls.",
            "spotify" => "This may stop music or downloads.",
            "steam" => "This may stop downloads, game launchers, or related game sessions.",
            _ => "This app will be asked to close. Save important work in it before continuing."
        };
    }

    private static string GetCloseWarning(
        string processName)
    {
        var normalized =
            NormalizeProcessName(processName);

        if (IsBrowserProcess(normalized))
        {
            return "Open tabs, downloads, private windows, or unsaved forms in this browser may close.";
        }

        return normalized switch
        {
            "code" => "Unsaved editor changes, terminals, and running local development tasks may stop.",
            "chatgpt" => "Active conversations, uploads, or generated content in the app window may close.",
            "discord" => "Voice calls, streams, chats, or uploads may disconnect.",
            "slack" => "Calls, messages, or uploads may be interrupted.",
            "teams" => "Meetings, calls, chats, or uploads may be interrupted.",
            "zoom" => "Meetings or calls may end.",
            "spotify" => "Playback or downloads may stop.",
            "steam" => "Downloads or game sessions may stop.",
            _ => "Open windows or unsaved work in this app may close."
        };
    }

    private static bool IsBrowserProcess(
        string processName)
    {
        return processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("brave", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("opera", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("opera_gx", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("vivaldi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedClosableUserApp(
        string processName)
    {
        if (IsBrowserProcess(processName))
        {
            return true;
        }

        return processName.Equals("code", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("chatgpt", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("discord", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("slack", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("teams", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("zoom", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("spotify", StringComparison.OrdinalIgnoreCase) ||
               processName.Equals("steam", StringComparison.OrdinalIgnoreCase);
    }

    public class OpenTargetRequest
    {
        public string Target { get; set; } = string.Empty;
    }

    [HttpPost("open-target")]
    [HttpPost("/api/remediation/open-target")]
    public IActionResult OpenTarget([FromBody] OpenTargetRequest payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Target))
        {
            return BadRequest(new { detail = "target is required" });
        }

        var normalizedTarget = payload.Target
            .Trim()
            .Replace("_", " ")
            .Replace("-", " ")
            .ToLowerInvariant();

        var (command, toolName) = normalizedTarget switch
        {
            "task manager" => ("taskmgr.exe", "Task Manager"),
            "device manager" => ("devmgmt.msc", "Device Manager"),
            "startup apps" => ("ms-settings:startupapps", "Startup Apps"),
            "reliability monitor" => ("perfmon /rel", "Reliability Monitor"),
            "storage settings" => ("ms-settings:storagesense", "Storage Settings"),
            "backup settings" => ("ms-settings:backup", "Backup Settings"),
            "power settings" => ("ms-settings:powersleep", "Power Settings"),
            _ => (string.Empty, string.Empty)
        };

        if (string.IsNullOrEmpty(command))
        {
            return Ok(new
            {
                success = false,
                summary = $"Unknown or unsupported verification target: {payload.Target}"
            });
        }

        try
        {
            var parts = command.Split(' ', 2);
            Process.Start(new ProcessStartInfo
            {
                FileName = parts[0],
                Arguments = parts.Length > 1 ? parts[1] : string.Empty,
                UseShellExecute = true
            });

            return Ok(new
            {
                success = true,
                summary = $"{toolName} opened for verification.",
                proof = new[]
                {
                    new
                    {
                        label = "Windows tool opened",
                        status = "completed",
                        meaning = $"RigMD opened {toolName}.",
                        after = toolName
                    }
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, summary = ex.Message });
        }
    }

    public class ToolInvocationRequest
    {
        public string ToolName { get; set; } = string.Empty;
        public JsonElement Arguments { get; set; }
        public string? SessionId { get; set; }
    }

    [HttpGet("tools")]
    public IActionResult GetRegisteredTools()
    {
        if (_toolRegistry == null)
        {
            return Ok(Array.Empty<object>());
        }

        var tools = _toolRegistry.GetAllTools().Select(t => new
        {
            name = t.Name,
            displayName = t.DisplayName,
            description = t.Description,
            safetyTier = t.SafetyTier.ToString(),
            functionDeclaration = t.GetFunctionDeclaration()
        });

        return Ok(tools);
    }

    [HttpPost("tools/preview")]
    public async Task<IActionResult> PreviewTool(
        [FromBody] ToolInvocationRequest request,
        CancellationToken cancellationToken)
    {
        if (_toolRegistry == null)
        {
            return StatusCode(503, new { message = "Tool registry is not configured." });
        }

        var tool = _toolRegistry.GetTool(request.ToolName);
        if (tool == null)
        {
            return NotFound(new { message = $"Tool '{request.ToolName}' is not registered." });
        }

        var preview = await tool.PreviewImpactAsync(request.Arguments, cancellationToken);
        return Ok(preview);
    }

    [HttpPost("tools/execute")]
    public async Task<IActionResult> ExecuteTool(
        [FromBody] ToolInvocationRequest request,
        CancellationToken cancellationToken)
    {
        if (_toolRegistry == null)
        {
            return StatusCode(503, new { message = "Tool registry is not configured." });
        }

        var tool = _toolRegistry.GetTool(request.ToolName);
        if (tool == null)
        {
            return NotFound(new { message = $"Tool '{request.ToolName}' is not registered." });
        }

        Action<string>? progressReporter = null;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            progressReporter = msg =>
            {
                _ = _hubContext.Clients
                    .Group(request.SessionId)
                    .SendAsync("ReceiveProgressUpdate", msg, cancellationToken);
            };
        }

        var result = await tool.ExecuteAsync(request.Arguments, progressReporter, cancellationToken);
        return Ok(result);
    }

    private sealed class CloseAppOutcome
    {
        public string ProcessName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public double BeforeMemoryMb { get; set; }
        public double AfterMemoryMb { get; set; }
        public int BeforeProcessCount { get; set; }
        public int ClosedWindows { get; set; }
        public int ForceClosedProcesses { get; set; }
        public int FailedProcesses { get; set; }

        public bool Success =>
            ClosedWindows > 0 ||
            ForceClosedProcesses > 0 ||
            AfterMemoryMb < BeforeMemoryMb;
    }
}
