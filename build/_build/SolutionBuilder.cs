using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.MSBuild;

public static class SolutionHelper
{
    public static void AddBuildSrcConfiguration(Solution solution)
    {
        var excludeProjects = new []
        {
            "Datadog.Trace.Tools.Runner.Tool",
            "Datadog.Trace.Tools.Runner.Standalone",
            "Datadog.Trace.ClrProfiler.Native",
            "Datadog.Trace.ClrProfiler.Native.DLL",
            "Datadog.Trace.Ci.Shared",
            "WindowsInstaller",
        };
        
        var regex = new Regex("src\\.*");
        var srcProjects = solution
            .AllProjects
            .Where(x => regex.IsMatch(x.Path))
            .Where(project => !excludeProjects.Contains(project.Name));

        TryAddSolutionConfiguration(
            solution,
            new SolutionConfiguration("SrcProjects|Any CPU"),
            srcProjects,
            "AnyCPU",
            Configuration.Release);
        solution.Save();
    }


    static bool TryAddSolutionConfiguration(
        Solution solution,
        SolutionConfiguration slnConfig,
        IEnumerable<Project> projectsToBuild,
        string platform,
        Configuration config)
    {
        if (!solution.Configurations.TryAdd(slnConfig, slnConfig))
        {
            Logger.Warn($"Solution configuration '{slnConfig}' already exists - skipping");
            return false;
        }
        
        // add the active cfg for every project
        foreach (var project in solution.AllProjects)
        {
            Logger.Info($"Adding '{slnConfig}' to project '{project.Name}'");
            project.Configurations.TryAdd($"{slnConfig}.ActiveCfg", $"{config}|{platform}");
        }
        
        // Enable the build for the projects
        foreach (var project in projectsToBuild)
        {
            Logger.Info($"Adding build requirement to project '{project.Name}'");
            project.Configurations.TryAdd($"{slnConfig}.Build.0", $"{config}|{platform}");
        }

        return true;
    } 
    
}
