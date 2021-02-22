using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.MSBuild;

internal static class DotNetSettingsExtensions
{
    public static DotNetBuildSettings SetTargetPlatform(this DotNetBuildSettings settings, MSBuildTargetPlatform platform)
    {
        return platform is null
            ? settings
            : settings.SetProperty("Platform", platform);
    }

    public static DotNetTestSettings SetTargetPlatform(this DotNetTestSettings settings, MSBuildTargetPlatform platform)
    {
        return platform is null
            ? settings
            : settings.SetProperty("Platform", platform);
    }
}
