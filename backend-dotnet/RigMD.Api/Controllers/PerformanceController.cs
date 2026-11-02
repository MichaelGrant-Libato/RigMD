using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;

namespace RigMD.Api.Controllers;

[ApiController, Route("api/performance")]
public sealed class PerformanceController(IPerformanceRecordRepository repository, PerformanceRecorder recorder,
    ICurrentClientProvider client, ILogger<PerformanceController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            var records = await repository.GetAsync(client.GetCurrentClientId(), cancellationToken);
            // Never export authorization identity, raw diagnostic values, paths, URLs or request bodies.
            return Ok(new { retentionLimit = PerformanceRules.RetainedRecords, health = recorder.Health, records = records.Select(r => new {
                r.Id, r.CreatedAt, r.Operation, r.Outcome, r.DurationMs, r.RecordCount, r.UnavailableCount, r.Mode, r.Engine,
                r.HttpStatus, r.ApiWorkingSetMb, r.ApiCpuTimeMs, r.AppVersion, r.BuildConfiguration, r.DeviceKey, r.OsVersion, r.Architecture, r.LogicalProcessors }) });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        { logger.LogError(ex, "Performance records unavailable."); return StatusCode(503, new { detail = "Performance records could not be loaded. Try again." }); }
    }

    [HttpPost, RequestSizeLimit(2048)]
    public IActionResult Save([FromBody] PerformanceSample sample)
    {
        if (!PerformanceRules.IsValid(sample) || !PerformanceRules.IsClientOperation(sample.Operation)
            || sample.HttpStatus != null || sample.ApiWorkingSetMb != null || sample.ApiCpuTimeMs != null)
            return BadRequest(new { detail = "Invalid performance measurement." });
        return recorder.TryRecord(client.GetCurrentClientId(), sample)
            ? Accepted(new { queued = true }) : StatusCode(503, new { detail = "Performance recorder is busy." });
    }
}
