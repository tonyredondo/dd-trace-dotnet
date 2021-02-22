using System;
using System.Collections.Generic;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.MSBuild;
using Nuke.Common.Tools.NuGet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.MSBuild.MSBuildTasks;

// #pragma warning disable SA1306  
// #pragma warning disable SA1134  
// #pragma warning disable SA1111  
// #pragma warning disable SA1400  
// #pragma warning disable SA1401  

[CheckBuildProjectConfigurations]
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Platform to build - x86 or x64. Default is unspecified")]
    readonly MSBuildTargetPlatform Platform;

    [Parameter("The location to publish the build output. Default is ./src/bin/managed-publish ")]
    readonly AbsolutePath PublishOutput;
    
    AbsolutePath PublishOutputPath => PublishOutput ?? (SourceDirectory / "bin" / "managed-publish");

    [Solution("Datadog.Trace.sln")] readonly Solution Solution;

    [Solution("Datadog.Trace.Native.sln")] readonly Solution NativeSolution;

    AbsolutePath SourceDirectory => RootDirectory / "src";

    AbsolutePath TestsDirectory => RootDirectory / "test";

    AbsolutePath MsBuildProject => RootDirectory / "Datadog.Trace.proj";

    Project ManagedLoaderProject => Solution.GetProject("Datadog.Trace.ClrProfiler.Managed.Loader");

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(PublishOutputPath);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            NuGetTasks.NuGetRestore(s => s
                .SetTargetPath(Solution)
                .SetVerbosity(NuGetVerbosity.Normal));
            DotNetRestore(s => s
                .SetProjectFile(Solution)
                .SetVerbosity(DotNetVerbosity.Normal));
        });

    Target CompileSolution => _ => _
        .After(Restore)
        .Executes(() =>
        {
            RootDirectory.GlobFiles(
                "src/**/*.csproj",
                "test/**/*.Tests.csproj",
                "test/benchmarks/**/*.csproj")
            .Where(path => !path.ToString().Contains("Datadog.Trace.Tools.Runner"))
            .ForEach(project => {
                DotNetBuild(config => config
                    .SetProjectFile(project)
                    .SetTargetPlatform(Platform)
                    .SetConfiguration(Configuration)
                    .SetProcessEnvironmentVariable("DD_SERVICE_NAME", "dd-tracer-dotnet"));
            });
        });

    Target CompileIntegrationTests => _ => _
        .After(CompileTracerHome)
        .Executes(() =>
        {
            RootDirectory.GlobFiles(
                "test/test-applications/regression/**/*.csproj",
                "test/*.IntegrationTests/*.IntegrationTests.csproj")
            .Where(path => !path.ToString().Contains("StackExchange.Redis.AssemblyConflict.LegacyProject")
                && !path.ToString().Contains("EntityFramework6x.MdTokenLookupFailure")
                && !path.ToString().Contains("ExpenseItDemo"))
            .ForEach(project => {
                DotNetBuild(config => config
                    .EnableNoRestore()
                    .SetProjectFile(project)
                    .SetTargetPlatform(Platform)
                    .SetConfiguration(Configuration)
                    .SetProperty("ManagedProfilerOutputDirectory", PublishOutputPath));
                // Need to add: /nowarn:netsdk1138
            });
        });

    Target CompileSamples => _ => _
        .After(CompileTracerHome)
        .After(CompileIntegrationTests)
        .Executes(() =>
        {
            RootDirectory.GlobFiles("test/test-applications/integrations/**/*.csproj")
            .Where(path => !path.ToString().Contains("dependency-libs"))
            .ForEach(project => {
                DotNetBuild(config => config
                    .EnableNoRestore()
                    .SetProjectFile(project)
                    .SetTargetPlatform(Platform)
                    .SetConfiguration(Configuration)
                    .SetProperty("BuildInParallel", "false")
                    .SetProperty("ManagedProfilerOutputDirectory", PublishOutputPath));
                // Need to add: /nowarn:netsdk1138
            });
        });

    Target CompileManagedLoader => _ => _
        .Executes(() =>
        {
            // Some build steps say to build these, but they don't exist, so probably aren't necessary
            // "sample-libs/**/Samples.ExampleLibrary*.csproj")
            DotNetBuild(config => config
                .SetProjectFile(ManagedLoaderProject)
                .SetTargetPlatform(Platform)
                .SetConfiguration(Configuration));
            // Need to add: /nowarn:netsdk1138
        });

    Target PublishManagedLoader => _ => _
        .After(CompileManagedLoader)
        .Executes(() =>
        {
            var frameworks = new[] { "netstandard2.0", "netcoreapp3.1" };

            DotNetPublish(config => config
                .SetProject(ManagedLoaderProject)
                .SetConfiguration(Configuration)
                .CombineWith(frameworks, (x, framework) => x
                    .SetOutput(PublishOutputPath / framework)
                    .SetFramework(framework)));
        });

    Target UnitTest => _ => _
        .After(CompileSolution)
        .Executes(() =>
        {
            RootDirectory.GlobFiles("test/**/*.Tests.csproj")
            .ForEach(project => {
                DotNetTest(x => x
                    .SetProjectFile(Solution)
                    .SetTargetPlatform(Platform)
                    .SetConfiguration(Configuration)
                    .SetProcessEnvironmentVariable("DD_SERVICE_NAME", "dd-tracer-dotnet"));
            });
        });
    
    Target IntegrationTests => _ => _
        .After(CompileIntegrationTests)
        .After(CompileSamples)
        .Executes(() =>
        {
            var projects = new[]
            {
                Solution.GetProject("Datadog.Trace.IntegrationTests"),
                Solution.GetProject("Datadog.Trace.OpenTracing.IntegrationTests"),
            };

            DotNetTest(x => x
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .CombineWith(projects, (y, project) => y
                    .SetProjectFile(project))
            );
        });

    Target ClrProfilerIntegrationTests => _ => _
        .After(CompileIntegrationTests)
        .After(CompileSamples)
        .Executes(() =>
        {
            var project = Solution.GetProject("Datadog.Trace.ClrProfiler.IntegrationTests");

            DotNetTest(x => x
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetProjectFile(project)
                .SetFilter("(RunOnWindows=True|Category=Smoke)&LoadFromGAC!=True&IIS!=True"));
        });

    Target RestoreNative => _ => _
        .Executes(() =>
        {
            NuGetTasks.NuGetRestore(s => s
                .SetTargetPath(NativeSolution)
                .SetVerbosity(NuGetVerbosity.Normal));
        });

    Target CompileNative => _ => _
        .Executes(() =>
        {
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetTargets("BuildCpp", "BuildCppTests")
                // /nowarn:netsdk1138
                .SetMaxCpuCount(null));
        });

    Target CompileTracerHome => _ => _
        .After(Restore)
        .Requires(() => Platform)
        .Requires(() => PublishOutputPath != null)
        .Executes(() =>
        {
            // this triggers a dependency chain that builds all the managed and native dlls
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetTargets("msi")
                .AddProperty("RunWixToolsOutOfProc", true)
                .SetProperty("TracerHomeDirectory", PublishOutputPath)
                .SetMaxCpuCount(null));
        });

    Target BuildFrameworkReproductions => _ => _
        .After(Restore)
        .Requires(() => PublishOutputPath != null)
        .Executes(() =>
        {
            // this triggers a dependency chain that builds all the managed and native dlls
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetTargets("BuildFrameworkReproductions")
                .SetMaxCpuCount(null));
        });

    Target RunNativeTests => _ => _
        .Executes(() =>
        {
            var workingDirectory = TestsDirectory / "Datadog.Trace.ClrProfiler.Native.Tests" / "bin" / Configuration.ToString() / Platform.ToString();
            var exePath = workingDirectory / "Datadog.Trace.ClrProfiler.Native.Tests.exe";
            var testExe = ToolResolver.GetLocalTool(exePath);
            testExe("--gtest_output=xml", workingDirectory: workingDirectory);
        });

    Target BuildLinuxProfiler => _ => _
        .DependsOn(CompileManagedLoader)
        .DependsOn(PublishManagedLoader);

    Target CiWindowsIntegrationTests => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .DependsOn(CompileTracerHome)
        .DependsOn(BuildFrameworkReproductions)
        .DependsOn(CompileIntegrationTests)
        .DependsOn(CompileSamples)
        .DependsOn(IntegrationTests)
        .DependsOn(ClrProfilerIntegrationTests);
        
    /// <summary>  
    /// Run the default build 
    /// </summary> 
    public static int Main() => Execute<Build>(x => x.CiWindowsIntegrationTests);  
}
