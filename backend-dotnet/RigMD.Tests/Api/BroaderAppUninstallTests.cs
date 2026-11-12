using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Controllers;
using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public sealed class BroaderAppUninstallTests
{
    private static StorageAppEntry App => new("Everyday Notes", "Everyday Software, Inc.", "1.2", 2048, "Current Windows account registration")
        { Id = new string('B', 64) };
    private const string Command = "\"C:\\Program Files\\Everyday Notes\\uninstall.exe\" --uninstall --product=notes";
    private const string Sid = "S-1-5-21-123-456-789-1001";
    private static StorageAppEntry Verified(string command = Command) => RegisteredAppUninstaller.Identify(App, command, null,
        _ => (new string('A', 64), "Everyday Software Inc"), () => Sid);

    [Fact]
    public void RegisteredUninstallerUsesProvidedAppMetadataWithoutNamedAppRules()
    {
        var verified = Verified();
        Assert.True(verified.CanUninstall);
        Assert.Equal("registered_exe", verified.UninstallMethod);
        Assert.True(RegisteredAppUninstaller.ValidTarget(verified.RegisteredTarget!));
        Assert.Equal(Sid, verified.RegisteredTarget!.UserSid);
        var json = JsonSerializer.Serialize(verified);
        Assert.DoesNotContain("Executable", json);
        Assert.DoesNotContain("UserSid", json);
        Assert.DoesNotContain("Sha256", json);
    }

    [Theory]
    [InlineData("cmd.exe /c uninstall.exe")]
    [InlineData("C:\\Windows\\System32\\cmd.exe /c uninstall.exe")]
    [InlineData("\\\\server\\share\\uninstall.exe")]
    [InlineData("%LOCALAPPDATA%\\uninstall.exe")]
    [InlineData("C:\\Programs\\uninstall.exe & calc.exe")]
    [InlineData("C:\\Programs\\uninstall.exe /command=arbitrary")]
    [InlineData("C:\\Programs\\uninstall.exe --force-uninstall")]
    [InlineData("C:\\Programs\\uninstall.exe C:\\payload.ps1")]
    [InlineData("C:\\Programs\\install.exe --repair")]
    [InlineData("C:\\Program Files\\Notes\\uninstall.exe")]
    [InlineData("C:\\Programs\\uninstall.exe:stream")]
    [InlineData("C:\\Programs\\uninstall.exe\n--uninstall")]
    public void UnsupportedCommandsNeverReachVerificationOrExecution(string command)
    {
        Assert.Null(RegisteredAppUninstaller.Parse(App, command, null));
        Assert.False(RegisteredAppUninstaller.Identify(App, command, null,
            _ => throw new Exception("Must not verify rejected commands"), () => throw new Exception("Must not request a token")).CanUninstall);
    }

    [Fact]
    public void MissingSignerMismatchProtectedAndNoRemoveAllFailClosed()
    {
        Assert.False(RegisteredAppUninstaller.Identify(App, Command, null, _ => (new string('A', 64), "Another Company"), () => Sid).CanUninstall);
        Assert.False(RegisteredAppUninstaller.Identify(App, Command, null, _ => throw new IOException(), () => Sid).CanUninstall);
        Assert.False(RegisteredAppUninstaller.Identify(App, Command, null, _ => (new string('A', 64), App.Publisher), () => null).CanUninstall);
        Assert.Null(RegisteredAppUninstaller.Parse(App, Command, 1));
        Assert.Null(RegisteredAppUninstaller.Parse(App with { Name = "Security Endpoint" }, Command, null));
        Assert.Null(RegisteredAppUninstaller.Parse(App with { Publisher = "" }, Command, null));
        Assert.False(RegisteredAppUninstaller.PublisherMatches("Everyday Software", "Everyday Software Impostor"));
    }

    [Fact]
    public void WindowsArgumentQuotingPreservesExactIndividualArguments()
    {
        var arguments = new[] { "--uninstall", "--product=notes with spaces", "quoted\"value", @"C:\Data with spaces\", "" };
        var command = "\"C:\\Programs\\uninstall.exe\" " + string.Join(" ", arguments.Select(RegisteredAppUninstaller.Quote));
        var parsed = RegisteredAppUninstaller.Parse(App, command, null);
        Assert.NotNull(parsed);
        Assert.Equal(arguments, JsonSerializer.Deserialize<string[]>(parsed.ArgumentsJson));
    }

    [Theory]
    [InlineData(Sid, Sid, 0x2000, 1, 1, true)]
    [InlineData(Sid, "S-1-5-18", 0x2000, 1, 1, false)]
    [InlineData(Sid, Sid, 0x3000, 1, 1, false)]
    [InlineData(Sid, Sid, 0x1000, 1, 1, false)]
    [InlineData(Sid, Sid, 0x2000, 1, 2, false)]
    [InlineData(null, null, 0x2000, 1, 1, false)]
    public void VendorUninstallersRequireTheSameNormalDesktopAccount(string? current, string? desktop, uint integrity, int session, int desktopSession, bool allowed) =>
        Assert.Equal(allowed, NativeUninstallRunner.AllowedIdentity(current, desktop, integrity, session, desktopSession));

    private static PackageFacts Package => new("Everyday.Notes_1.0_x64__abc", "Everyday.Notes_abc", Sid, "Everyday Notes", "Everyday Software", "1.0",
        false, false, false, false, false, false, true, true, true, false);

    [Fact]
    public void OrdinaryStorePackageHasCurrentUserRemovalWithoutInventedSize()
    {
        var app = WindowsPackageInventory.FromFacts(Package);
        Assert.True(app.CanUninstall);
        Assert.Equal("windows_package", app.UninstallMethod);
        Assert.Equal(Sid, app.PackageTarget!.UserSid);
        Assert.Null(app.EstimatedBytes);
        Assert.Null(app.ProductCode);
        Assert.NotEqual(app.Id, WindowsPackageInventory.FromFacts(Package with { UserSid = "another-user" }).Id);
        Assert.DoesNotContain("PackageTarget", JsonSerializer.Serialize(app));
    }

    [Fact]
    public void SharedProtectedUnknownAndNonApplicationPackagesCannotBeRemoved()
    {
        var cases = new[] { Package with { Framework = true }, Package with { Resource = true }, Package with { Bundle = true },
            Package with { Optional = true }, Package with { Development = true }, Package with { SystemSigned = true },
            Package with { SignatureVerified = false }, Package with { Healthy = false }, Package with { HasAppEntry = false },
            Package with { RequiredByAnotherPackage = true }, Package with { UserSid = "" }, Package with { FullName = "" },
            Package with { Name = "Windows Security" } };
        foreach (var facts in cases)
        {
            var app = WindowsPackageInventory.FromFacts(facts);
            Assert.False(app.CanUninstall);
            Assert.Null(app.PackageTarget);
        }
    }

    [Fact]
    public void ExitCannotRaceARunningActionOrAllowAnActionAfterExitApproval()
    {
        var gate = new LocalRemediationExecutionGate();
        var controller = new DesktopLifetimeController(gate);
        using (var lease = gate.TryEnter())
        {
            Assert.NotNull(lease);
            Assert.IsType<ConflictObjectResult>(controller.PrepareExit());
        }
        Assert.IsType<OkObjectResult>(controller.PrepareExit());
        Assert.Null(gate.TryEnter());
        Assert.True(gate.TryBeginShutdown());
    }

    [Theory]
    [InlineData(0, "absent", "removed")]
    [InlineData(0, "installed", "unverified")]
    [InlineData(3010, "absent", "unverified")]
    [InlineData(1602, "installed", "failed")]
    [InlineData(1223, "installed", "canceled")]
    public void VendorExitCodesDoNotInheritWindowsInstallerSuccessSemantics(int code, string status, string expected)
    {
        var before = new AppUninstallSnapshot(DateTimeOffset.UtcNow, "installed", []);
        var operation = new AppUninstallOperation("operation", Guid.NewGuid(), "storage_usage_C:", Verified(), "running", "", before);
        var result = AppUninstallService.Assess(operation, code, before with { InstallerStatus = status });
        Assert.Equal(expected, result.Status);
        Assert.False(result.RebootRequired);
    }
}
