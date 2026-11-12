using RigMD.Application.Services;
using RigMD.Domain.Entities;
using System.Text.Json;

namespace RigMD.Tests.Application;

public class StorageFindingActionTests
{
    [Theory]
    [InlineData("recycle_selected_files")]
    [InlineData("empty_recycle_bin")]
    public void PersonalCleanupRequiresMeasuredStorageProofAndRetainsHardwareRestrictions(string tool)
    {
        var check = Check();
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", tool));
        check.Session.Answers.First(answer => answer.QuestionKey == "proof").AnswerValue = "[{\"metric_id\":\"storage_usage_C:\",\"status\":\"high\",\"value\":\"90% used\",\"evidence_kind\":\"measurement\"}]";
        Assert.NotNull(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", tool));
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_D:", tool));
        check.DiagnosedCategory = "Storage health behavior";
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", tool));
    }
    [Theory]
    [InlineData("measurement", true)]
    [InlineData("", false)]
    [InlineData("reported_status", false)]
    [InlineData("unavailable", false)]
    public void UninstallRequiresMeasuredStorageEvidence(string kind, bool allowed)
    {
        var check = Check();
        check.Session.Answers.First(answer => answer.QuestionKey == "proof").AnswerValue = JsonSerializer.Serialize(new[]
        {
            new { metric_id = "storage_usage_C:", status = "high", value = "90% used", evidence_kind = kind }
        });
        Assert.Equal(allowed, RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", "uninstall_installed_app") != null);
    }

    private static DiagnosticOutput Check(string status = "high", string value = "90% used", string mode = "full", string components = "[]", string scenario = "") => new()
    {
        DiagnosedCategory = "High Memory Pressure",
        Session = new() { Answers = new List<SessionAnswer>
        {
            new() { QuestionKey = "diagnosis_mode", AnswerValue = mode },
            new() { QuestionKey = "component_ids", AnswerValue = components },
            new() { QuestionKey = "scenario_id", AnswerValue = scenario },
            new() { QuestionKey = "proof", AnswerValue = JsonSerializer.Serialize(new[] { new { metric_id = "storage_usage_C:", status, value } }) }
        } }
    };

    [Fact]
    public void StorageSelectionUsesSavedEvidenceWithoutRewritingTheOriginalMemoryDiagnosis()
    {
        var original = Check();
        var selected = RepairSupportPolicy.SelectStorageFinding(original, "storage_usage_C:", "clear_temp_files");
        Assert.NotNull(selected);
        Assert.Equal("Elevated Storage Utilization", selected.DiagnosedCategory);
        Assert.Equal("High Memory Pressure", original.DiagnosedCategory);
        Assert.Equal(original.Id, selected.Id);
        Assert.Null(RepairSupportPolicy.ScopeBlockReason(selected, "clear_temp_files"));
        Assert.Null(RepairSupportPolicy.ToolMismatchReason(selected.DiagnosedCategory, "clear_temp_files"));
    }

    [Theory]
    [InlineData("normal", "90% used")]
    [InlineData("unknown", "90% used")]
    [InlineData("high", "79.9% used")]
    [InlineData("high", "101% used")]
    [InlineData("high", "Unavailable")]
    public void UnverifiedOrInconsistentWarningsCannotAuthorizeCleanup(string status, string value)
        => Assert.Null(RepairSupportPolicy.SelectStorageFinding(Check(status, value), "storage_usage_C:", "clear_temp_files"));

    [Theory]
    [InlineData("component", "[\"memory\"]", "")]
    [InlineData("component", "[\"storage\"", "")]
    [InlineData("scenario", "[]", "app-crashes")]
    [InlineData("unknown", "[]", "")]
    public void AStorageWarningCannotBroadenTheSavedCheckScope(string mode, string components, string scenario)
        => Assert.Null(RepairSupportPolicy.SelectStorageFinding(Check(mode: mode, components: components, scenario: scenario), "storage_usage_C:", "clear_temp_files"));

    [Fact]
    public void LatestEvidenceAndSelectedMetricAreRequired()
    {
        var check = Check();
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_D:", "clear_temp_files"));
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", "run_system_file_checker"));
        check.Session.Answers.Add(new() { QuestionKey = "resolution_proof", AnswerValue = "[{\"metric_id\":\"memory_usage\",\"value\":\"90%\",\"status\":\"high\"}]" });
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", "clear_temp_files"));
    }

    [Theory]
    [InlineData("Storage health behavior")]
    [InlineData("Application crash history requires review")]
    [InlineData("Scanner evidence unavailable")]
    public void StorageSelectionCannotBypassHardwareOrReviewOnlyRules(string category)
    {
        var check = Check(); check.DiagnosedCategory = category;
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", "clear_temp_files"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[null]")]
    [InlineData("[{\"metric_id\":\"storage_usage_C:\",\"status\":\"high\",\"value\":\"90%\"},{\"metric_id\":\"storage_usage_C:\",\"status\":\"high\",\"value\":\"90%\"}]")]
    [InlineData("[{\"metric_id\":\"storage_usage_C:\",\"status\":\"high\",\"value\":\"90%\"},{\"metric_id\":\"storage_health\",\"status\":\"critical\",\"value\":\"Warning\"}]")]
    public void MalformedDuplicateAndDriveHealthEvidenceCannotAuthorizeCleanup(string latest)
    {
        var check = Check();
        check.Session.Answers.Add(new() { QuestionKey = "resolution_proof", AnswerValue = latest });
        Assert.Null(RepairSupportPolicy.SelectStorageFinding(check, "storage_usage_C:", "clear_temp_files"));
    }
}
