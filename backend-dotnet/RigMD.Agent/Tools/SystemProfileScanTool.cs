using RigMD.Application.Contracts.Providers;

namespace RigMD.Agent.Tools;

public class SystemProfileScanTool : IAgentTool
{
    private readonly IWindowsSystemProfileService _profileService;

    public SystemProfileScanTool(
        IWindowsSystemProfileService profileService)
    {
        _profileService = profileService;
    }

    public string Id => "scan_system_profile";

    public string Description =>
        "Reads the complete current Windows system profile.";

    public object Execute()
    {
        return _profileService.GetLiveSystemProfile();
    }
}