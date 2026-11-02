using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Api.Services;

public sealed class PerformanceRecorder(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PerformanceRecorder> logger) : BackgroundService
{
    private readonly Channel<PerformanceRecord> _queue = Channel.CreateBounded<PerformanceRecord>(new BoundedChannelOptions(256)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private long _queued, _saved, _dropped, _storageFailures;
    private readonly string _version = typeof(PerformanceRecorder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    private readonly string _configuration = typeof(PerformanceRecorder).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";
    // Identifies this test machine without exporting its Windows name, account, serials or network addresses.
    private readonly string _deviceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("RigMD-performance:" + Environment.MachineName)))[..16];

    public object Health => new { queued = Interlocked.Read(ref _queued), saved = Interlocked.Read(ref _saved),
        dropped = Interlocked.Read(ref _dropped), storageFailures = Interlocked.Read(ref _storageFailures) };

    public bool TryRecord(string clientId, PerformanceSample sample)
    {
        if (string.IsNullOrWhiteSpace(clientId) || !PerformanceRules.IsValid(sample)) return false;
        var row = new PerformanceRecord { Id = sample.Id, ClientId = clientId, CreatedAt = clock.GetUtcNow(),
            Operation = sample.Operation, Outcome = sample.Outcome, DurationMs = sample.DurationMs,
            RecordCount = sample.RecordCount, UnavailableCount = sample.UnavailableCount, Mode = sample.Mode, Engine = sample.Engine,
            HttpStatus = sample.HttpStatus, ApiWorkingSetMb = sample.ApiWorkingSetMb, ApiCpuTimeMs = sample.ApiCpuTimeMs,
            AppVersion = _version, BuildConfiguration = _configuration, DeviceKey = _deviceKey,
            OsVersion = Environment.OSVersion.VersionString, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), LogicalProcessors = Environment.ProcessorCount };
        if (_queue.Writer.TryWrite(row)) { Interlocked.Increment(ref _queued); return true; }
        Interlocked.Increment(ref _dropped);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var row in _queue.Reader.ReadAllAsync())
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IPerformanceRecordRepository>().AddAsync(row);
                Interlocked.Increment(ref _saved);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _storageFailures);
                logger.LogWarning(ex, "A local performance record could not be saved.");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        // .NET can schedule ExecuteAsync after StartAsync returns. Drain accepted records before
        // base.StopAsync cancels that scheduled task, while honoring the host's shutdown budget.
        if (ExecuteTask != null)
        {
            try { await ExecuteTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        await base.StopAsync(cancellationToken);
    }
}
