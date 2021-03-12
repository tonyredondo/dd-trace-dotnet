using System;
using System.Collections.Generic;
using System.IO;
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
using static Nuke.Common.IO.CompressionTasks;
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

    [Parameter("Configuration to build - Default is 'Release'")]
    readonly Configuration Configuration = Configuration.Release;

    [Parameter("Platform to build - x86 or x64. Default is x64")]
    readonly MSBuildTargetPlatform Platform = MSBuildTargetPlatform.x64;

    [Parameter("The location to publish the build output. Default is ./src/bin/managed-publish ")]
    readonly AbsolutePath PublishOutput;
    
    [Parameter("The location to create the tracer home directory. Default is ./src/bin/tracer-home ")]
    readonly AbsolutePath TracerHome;
    [Parameter("The location to place NuGet packages and other packages. Default is ./src/bin/artifiacts ")]
    readonly AbsolutePath Artifacts;
    
    [Parameter("The location to restore Nuget packages (optional) ")]
    readonly AbsolutePath NugetManagedCacheFolder;
    
    [Solution("Datadog.Trace.sln")] readonly Solution Solution;
    [Solution("Datadog.Trace.Native.sln")] readonly Solution NativeSolution;
    AbsolutePath MsBuildProject => RootDirectory / "Datadog.Trace.proj";

    AbsolutePath PublishOutputPath => PublishOutput ?? (SourceDirectory / "bin" / "managed-publish");
    AbsolutePath TracerHomeDirectory => TracerHome ?? (RootDirectory / "bin" / "tracer-home");
    AbsolutePath ArtifactsDirectory => Artifacts ?? (RootDirectory / "bin" / "artifacts");
    AbsolutePath TracerHomeZip => ArtifactsDirectory / "tracer-home.zip";

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "test";
    
    Project ManagedLoaderProject => Solution.GetProject("Datadog.Trace.ClrProfiler.Managed.Loader");
    Project ManagedProfilerProject => Solution.GetProject("Datadog.Trace.ClrProfiler.Managed");
    Project NativeProfilerProject => Solution.GetProject("Datadog.Trace.ClrProfiler.Native");

    IEnumerable<MSBuildTargetPlatform> ArchitecturesForPlatform =>
        Equals(Platform, MSBuildTargetPlatform.x64)
            ? new[] {MSBuildTargetPlatform.x64, MSBuildTargetPlatform.x86}
            : new[] {MSBuildTargetPlatform.x86};
    
    IEnumerable<Project> NuGetPackages => new []
    {
        Solution.GetProject("Datadog.Trace"),
        Solution.GetProject("Datadog.Trace.OpenTracing"),
    };
    
    IEnumerable<TargetFramework> TargetFrameworks = new []
    {
        TargetFramework.NET45, 
        TargetFramework.NET461,
        TargetFramework.NETSTANDARD2_0, 
        TargetFramework.NETCOREAPP3_1,
    };

    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(PublishOutputPath);
            EnsureCleanDirectory(TracerHomeDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
            DeleteFile(TracerHomeZip);
        });

    Target RestoreNuGet => _ => _
        .After(Clean)
        .Executes(() =>
        {
            NuGetTasks.NuGetRestore(s => s
                .SetTargetPath(Solution)
                .SetVerbosity(NuGetVerbosity.Normal));
        });

    Target RestoreNative => _ => _
        .After(Clean)
        .Executes(() =>
        {
            NuGetTasks.NuGetRestore(s => s
                .SetTargetPath(NativeSolution)
                .SetVerbosity(NuGetVerbosity.Detailed));
        });
 
    Target RestoreDotNet => _ => _
        .After(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution)
                .SetVerbosity(DotNetVerbosity.Normal)
                .SetTargetPlatform(Platform)
                .SetProperty("configuration", Configuration.ToString())
                .SetNoWarnDotNetCore3()
                .When(!string.IsNullOrEmpty(NugetManagedCacheFolder), o => 
                        o.SetPackageDirectory(NugetManagedCacheFolder)));
        });

    Target Restore => _ => _
        .After(Clean)
        // .DependsOn(RestoreDotNet)
        .DependsOn(RestoreNuGet);

    Target CompileManagedSrc => _ => _
        .After(Restore)
        .After(RestoreNuGet)
        .Executes(() =>
        {
            // Always AnyCPU
            MSBuild(x => x
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .DisableRestore()
                .SetTargets("BuildCsharpSrc")
            );
        });
    
    Target CompileManagedUnitTests => _ => _
        .After(Restore)
        .After(RestoreNuGet)
        .After(CompileManagedSrc)
        .Executes(() =>
        {
            MSBuild(x => x
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .DisableRestore()
                .SetProperty("BuildProjectReferences", false)
                .SetTargets("BuildCsharpUnitTests")
                .SetVerbosity(MSBuildVerbosity.Detailed)
                .CombineWith(ArchitecturesForPlatform, (x, arch) => x
                    .SetTargetPlatform(arch)));
        });

    Target CompileIntegrationTests => _ => _
        .After(CompileFrameworkReproductions)
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
                    // .EnableNoRestore()
                    .SetProjectFile(project)
                    // .SetTargetPlatform(Platform)
                    .SetConfiguration(Configuration)
                    //.SetNoDependencies(true)
                    .SetNoWarnDotNetCore3()
                    .SetProperty("ExcludeManagedProfiler", true)
                    .SetProperty("ExcludeNativeProfiler", true)
                    .SetProperty("LoadManagedProfilerFromProfilerDirectory", false)
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
                    // .EnableNoRestore()
                    .SetProjectFile(project)
                    .SetTargetPlatform(Platform)
                    .SetConfiguration(Configuration)
                    .SetNoDependencies(true)
                    .SetNoWarnDotNetCore3()
                    .SetProperty("BuildInParallel", "false")
                    .SetProperty("ManagedProfilerOutputDirectory", PublishOutputPath));
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
                .SetConfiguration(Configuration)
                .SetNoDependencies(true)
                .SetNoWarnDotNetCore3());
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

    Target PackNuGet => _ => _
        .After(CompileManagedSrc)
        .Executes(() =>
        {
            DotNetPack(s => s
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .CombineWith(NuGetPackages, (x, project) => x
                    .SetProject(project)));
        });

    Target RunUnitTests => _ => _
        .After(CompileManagedUnitTests)
        .Executes(() =>
        {
            var permutations =
                from arch in ArchitecturesForPlatform
                from project in RootDirectory.GlobFiles("test/**/*.Tests.csproj")
                select new {arch, project};

            DotNetTest(x => x
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetDDEnvironmentVariables()
                .CombineWith(permutations, (x, p) => x
                    .SetTargetPlatform(p.arch)
                    .SetProjectFile(p.project)));
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

    Target CompileNativeSrcWindows => _ => _
        .After(CompileManagedSrc)
        .Executes(() =>
        {
            // If we're building for x64, build for x86 too 
            var platforms =
                Equals(Platform, MSBuildTargetPlatform.x64)
                    ? new[] { MSBuildTargetPlatform.x64, MSBuildTargetPlatform.x86 }
                    : new[] { MSBuildTargetPlatform.x86 };

            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargets("BuildCppSrc")
                .DisableRestore()
                .SetMaxCpuCount(null)
                .CombineWith(platforms, (m, platform) => m
                    .SetTargetPlatform(platform)));
        });

    Target CompileMsi => _ => _
        .After(CompileNativeSrcWindows)
        .After(CompileManagedSrc)
        .Executes(() =>
        {
            // If we're building for x64, build for x86 too 
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargets("msi")
                .CombineWith(ArchitecturesForPlatform, (m, architecture) => m
                    .SetTargetPlatform(architecture)));
        });
    
    Target BuildTracerHome => _ => _
        .After(CompileMsi)
        .Executes(() =>
        {
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargets("CreateHomeDirectory")
                .SetProperty("Platform", "All"));
        });

    Target PublishManagedProfiler => _ => _
        .After(CompileManagedSrc)
        .Executes(() =>
        {
            DotNetPublish(s => s
                .SetProject(ManagedProfilerProject)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .CombineWith(TargetFrameworks, (p, framework) => p
                    .SetFramework(framework)
                    .SetOutput(TracerHomeDirectory / framework)));
        });
    
    Target PublishNativeProfilerWindows => _ => _
        .After(CompileNativeSrcWindows)
        .Executes(() =>
        {
            foreach (var architecture in ArchitecturesForPlatform)
            {
                var source = NativeProfilerProject.Directory / "bin" / Configuration / architecture.ToString() /
                             $"{NativeProfilerProject.Name}.dll";
                var dest = TracerHomeDirectory / $"win-{architecture}";
                Logger.Info($"Copying '{source}' to '{dest}'");
                CopyFileToDirectory(source, dest, FileExistsPolicy.OverwriteIfNewer);
            }
        });
    
    Target CopyIntegrationsJson => _ => _
        .After(PublishManagedProfiler)
        .Executes(() =>
        {
            var source = RootDirectory / "integrations.json";
            var dest = TracerHomeDirectory;

            Logger.Info($"Copying '{source}' to '{dest}'");
            CopyFileToDirectory(source, dest, FileExistsPolicy.OverwriteIfNewer);
        });

    Target BuildTracerHomeWindows => _ => _
        .DependsOn(PublishManagedProfiler)
        .DependsOn(PublishNativeProfilerWindows)
        .DependsOn(CopyIntegrationsJson);
    
    Target ZipTracerHome => _ => _
        .After(PublishManagedProfiler)
        .After(PublishNativeProfilerWindows)
        .After(CopyIntegrationsJson)
        .After(BuildTracerHomeWindows)
        .Executes(() =>
        {
            CompressZip(TracerHomeDirectory, TracerHomeZip, fileMode: FileMode.Create);
        });

    Target BuildMsi => _ => _
        .After(BuildTracerHomeWindows)
        .Executes(() =>
        {
            // this triggers a dependency chain that builds all the managed and native dlls
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargets("BuildMsi")
                .AddProperty("RunWixToolsOutOfProc", true)
                .SetProperty("TracerHomeDirectory", TracerHomeDirectory)
                .SetMaxCpuCount(null)
                .CombineWith(ArchitecturesForPlatform, (o, arch) => o
                    .SetProperty("MsiOutputPath", ArtifactsDirectory / arch.ToString() )
                    .SetTargetPlatform(arch)));
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

    Target CompileFrameworkReproductions => _ => _
        .After(CompileManagedSrc)
        .After(CompileNativeSrcWindows)
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
        .DependsOn(CompileFrameworkReproductions)
        .DependsOn(CompileIntegrationTests)
        .DependsOn(CompileSamples)
        .DependsOn(IntegrationTests)
        .DependsOn(ClrProfilerIntegrationTests);

    Target CleanBuild => _ =>
        _.DependsOn(Clean)
            .DependsOn(Restore)
            .DependsOn(CompileManagedSrc)
            .DependsOn(CompileNativeSrcWindows)
            .DependsOn(CompileFrameworkReproductions)
            .DependsOn(CompileIntegrationTests)
            .DependsOn(CompileSamples);

    Target LocalBuild => _ =>
        _
            .Description("Builds the tracer as described in the README")
            .DependsOn(Clean)
            .DependsOn(Restore)
            .DependsOn(CompileManagedSrc)
            .DependsOn(PackNuGet)
            .DependsOn(CompileNativeSrcWindows)
            .DependsOn(CompileMsi)
            .DependsOn(BuildTracerHome);

    Target WindowsFullCiBuild => _ =>
        _
            .Description("Convenience method for running the same build steps as the full Windows CI build")
            .DependsOn(Clean)
            .DependsOn(RestoreNuGet)
            .DependsOn(CompileManagedSrc)
            .DependsOn(CompileNativeSrcWindows)
            .DependsOn(BuildTracerHomeWindows)
            .DependsOn(ZipTracerHome)
            .DependsOn(PackNuGet)
            .DependsOn(BuildMsi)
            .DependsOn(CompileManagedUnitTests);
    

    /// <summary>  
    /// Run the default build 
    /// </summary> 
    public static int Main() => Execute<Build>(x => x.LocalBuild);
}
