using System.Text;
using RigMD.QuotaChecks;

if (args.Length != 0 || Console.IsInputRedirected)
{
    Console.Error.WriteLine("Run interactively without arguments. Do not put keys in commands, files, environment variables, or chat.");
    return 2;
}

Console.WriteLine("RigMD staging-only quota concurrency check");
Console.WriteLine($"Pinned project: {QuotaConcurrencyTester.StagingProjectReference}");
Console.WriteLine("No Gemini calls, real user accounts, database password, or production changes.");
Console.WriteLine("Uses at most 18 RPC calls and leaves only dummy quota counters in staging.");
Console.Write("Type RigMD-Staging to continue: ");
if (!string.Equals(Console.ReadLine(), "RigMD-Staging", StringComparison.Ordinal))
{
    Console.WriteLine("Cancelled. No requests sent.");
    return 2;
}

Console.Write("Paste the STAGING sb_secret_ key (input hidden), then press Enter: ");
var input = new StringBuilder();
while (true)
{
    var key = Console.ReadKey(intercept: true);
    if (key.Key == ConsoleKey.Enter) break;
    if (key.Key == ConsoleKey.Escape)
    {
        Console.WriteLine("\nCancelled. No requests sent.");
        return 2;
    }
    if (key.Key == ConsoleKey.Backspace)
    {
        if (input.Length > 0) input.Length--;
    }
    else if (!char.IsControl(key.KeyChar) && input.Length < 1024) input.Append(key.KeyChar);
}
Console.WriteLine();
var secret = input.ToString().Trim();
input.Clear();
if (!secret.StartsWith("sb_secret_", StringComparison.Ordinal) || secret.Length < 20)
{
    Console.Error.WriteLine("Expected a staging sb_secret_ key. No requests sent. Do not use a publishable key or Gemini key.");
    return 2;
}

using var handler = new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    MaxConnectionsPerServer = 16,
    ConnectTimeout = TimeSpan.FromSeconds(5)
};
using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
client.DefaultRequestHeaders.Add("apikey", secret);
secret = string.Empty;
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; timeout.Cancel(); };
try
{
    Console.WriteLine("Testing. A clean minute window may require a short wait; do not run another copy.");
    var result = await new QuotaConcurrencyTester(client).RunAsync(timeout.Token);
    Console.WriteLine($"PASS: {result.Requests} overlapping requests: {result.Allowed} allowed, {result.Denied} denied.");
    Console.WriteLine("PASS: exhausted account's follow-up denied; another dummy account allowed.");
    Console.WriteLine("The RPC uses HTTP 200 with allowed=false for denial; this is not an HTTP 429 test of the Gemini proxy.");
    return 0;
}
catch (QuotaCheckException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled or timed out. No concurrency pass recorded. Retry later, without changing production.");
    return 1;
}
catch (Exception)
{
    Console.Error.WriteLine("The staging network/test request failed. No pass recorded; raw errors and credentials are not displayed.");
    return 1;
}
finally
{
    client.DefaultRequestHeaders.Clear();
}
