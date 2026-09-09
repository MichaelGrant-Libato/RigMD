using System;
using System.Linq;
using Xunit;
using RigMD.Application.Services.Autonomy;

namespace RigMD.Tests.Application;

public class RemediationRegistryTests
{
    [Fact]
    public void GetAction_WhenActionExists_ReturnsValidDefinition()
    {
        // Arrange
        var registry = new RemediationRegistry();

        // Act
        var action = registry.GetAction("clear_user_temp_files");

        // Assert
        Assert.NotNull(action);
        Assert.Equal("Clear User Temp Files", action.Name);
        Assert.Equal("Low", action.RiskLevel);
    }

    [Fact]
    public void GetAction_WhenActionDoesNotExist_ReturnsNull()
    {
        // Arrange
        var registry = new RemediationRegistry();

        // Act
        var action = registry.GetAction("format_c_drive");

        // Assert
        Assert.Null(action);
    }

    [Fact]
    public void GetAllActions_ReturnsHardcodedList()
    {
        // Arrange
        var registry = new RemediationRegistry();

        // Act
        var actions = registry.GetAllActions().ToList();

        // Assert
        Assert.Equal(7, actions.Count);
        Assert.Contains(actions, a => a.Id == "clear_user_temp_files");
        Assert.Contains(actions, a => a.Id == "restart_explorer");
        Assert.Contains(actions, a => a.Id == "flush_dns");
        Assert.Contains(actions, a => a.Id == "clear_browser_cache");
        Assert.Contains(actions, a => a.Id == "clear_windows_update_cache");
        Assert.Contains(actions, a => a.Id == "run_disk_cleanup");
        Assert.Contains(actions, a => a.Id == "run_sfc_scan");
    }
}
