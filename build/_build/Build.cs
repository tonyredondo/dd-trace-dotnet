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
using static CustomDotNetTasks;

// #pragma warning disable SA1306  
// #pragma warning disable SA1134  
// #pragma warning disable SA1111  
// #pragma warning disable SA1400  
// #pragma warning disable SA1401  

partial class Build : NukeBuild
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

    [Parameter("The location to create the tracer home directory. Default is ./bin/tracer-home ")]
    readonly AbsolutePath TracerHome;
    [Parameter("The location to place NuGet packages and other packages. Default is ./bin/artifacts ")]
    readonly AbsolutePath Artifacts;
    
    [Parameter("The location to restore Nuget packages (optional) ")]
    readonly AbsolutePath NugetPackageDirectory;
    
    [Solution("Datadog.Trace.sln")] readonly Solution Solution;
    [Solution("Datadog.Trace.Native.sln")] readonly Solution NativeSolution;
    AbsolutePath MsBuildProject => RootDirectory / "Datadog.Trace.proj";

    AbsolutePath OutputDirectory => RootDirectory / "bin";
    AbsolutePath TracerHomeDirectory => TracerHome ?? (OutputDirectory / "tracer-home");
    AbsolutePath ArtifactsDirectory => Artifacts ?? (OutputDirectory / "artifacts");
    AbsolutePath TracerHomeZip => ArtifactsDirectory / "tracer-home.zip";

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "test";
    
    Project ManagedLoaderProject => Solution.GetProject(Projects.ClrProfilerManagedLoader);
    Project ManagedProfilerProject => Solution.GetProject(Projects.ClrProfilerManaged);
    Project NativeProfilerProject => Solution.GetProject(Projects.ClrProfilerNative);
    Project WindowsInstallerProject => Solution.GetProject(Projects.WindowsInstaller);

    [LazyPathExecutable(name: "cmake")] readonly Lazy<Tool> CMake;
    [LazyPathExecutable(name: "make")] readonly Lazy<Tool> Make;
    
    IEnumerable<MSBuildTargetPlatform> ArchitecturesForPlatform =>
        Equals(Platform, MSBuildTargetPlatform.x64)
            ? new[] {MSBuildTargetPlatform.x64, MSBuildTargetPlatform.x86}
            : new[] {MSBuildTargetPlatform.x86};
    
    IEnumerable<Project> NuGetPackages => new []
    {
        Solution.GetProject("Datadog.Trace"),
        Solution.GetProject("Datadog.Trace.OpenTracing"),
    };
    
    readonly IEnumerable<TargetFramework> TargetFrameworks = new []
    {
        TargetFramework.NET45, 
        TargetFramework.NET461,
        TargetFramework.NETSTANDARD2_0, 
        TargetFramework.NETCOREAPP3_1,
    };

    Target Clean => _ => _
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => EnsureCleanDirectory(x));
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => EnsureCleanDirectory(x));
            EnsureCleanDirectory(OutputDirectory);
            EnsureCleanDirectory(TracerHomeDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
            DeleteFile(TracerHomeZip);
        });

    Target CreateRequiredDirectories => _ => _
        .Unlisted()
        .Executes(() =>
        {
            EnsureExistingDirectory(TracerHomeDirectory);
            EnsureExistingDirectory(ArtifactsDirectory);
        });

    Target RestoreNuGet => _ => _
        .Unlisted()
        .After(Clean)
        .Executes(() =>
        {
            NuGetTasks.NuGetRestore(s => s
                .SetTargetPath(Solution)
                .SetVerbosity(NuGetVerbosity.Normal)
                .When(!string.IsNullOrEmpty(NugetPackageDirectory), o =>
                    o.SetPackagesDirectory(NugetPackageDirectory)));
        });
 
    Target RestoreDotNet => _ => _
        .Unlisted()
        .After(Clean)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution)
                .SetVerbosity(DotNetVerbosity.Normal)
                .SetTargetPlatform(Platform) // necessary to ensure we restore every project
                .SetProperty("configuration", Configuration.ToString())
                .When(!string.IsNullOrEmpty(NugetPackageDirectory), o =>
                        o.SetPackageDirectory(NugetPackageDirectory)));
        });

    Target Restore => _ => _
        .After(Clean)
        .DependsOn(CreateRequiredDirectories)
        // .DependsOn(RestoreDotNet)
        .DependsOn(RestoreNuGet);

    Target CompileManagedSrc => _ => _
        .Description("Compiles the managed code in the src directory")
        .DependsOn(CreateRequiredDirectories)
        .DependsOn(Restore)
        .Executes(() =>
        {
            // Always AnyCPU
            DotNetMSBuild(x => x
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .DisableRestore()
                .SetTargets("BuildCsharpSrc")
            );
        });

    Target PackNuGet => _ => _
        .Description("Creates the NuGet packages from the compiled src directory")
        .DependsOn(CreateRequiredDirectories)
        .DependsOn(CompileManagedSrc)
        .Executes(() =>
        {
            DotNetPack(s => s
                    .EnableNoRestore()
                    .EnableNoBuild()
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(ArtifactsDirectory / "nuget")
                    .CombineWith(NuGetPackages, (x, project) => x
                        .SetProject(project)),
                degreeOfParallelism: 2);
        });

    Target CompileNativeSrcWindows => _ => _
        .Unlisted()
        .DependsOn(CompileManagedSrc)
        .OnlyWhenStatic(() => IsWin)
        .Executes(() =>
        {
            // If we're building for x64, build for x86 too
            var platforms =
                Equals(Platform, MSBuildTargetPlatform.x64)
                    ? new[] { MSBuildTargetPlatform.x64, MSBuildTargetPlatform.x86 }
                    : new[] { MSBuildTargetPlatform.x86 };

            // Can't use dotnet msbuild, as needs to use the VS version of MSBuild
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargets("BuildCppSrc")
                .DisableRestore()
                .SetMaxCpuCount(null)
                .CombineWith(platforms, (m, platform) => m
                    .SetTargetPlatform(platform)));
        });

    Target CompileNativeSrcLinux => _ => _
        .Unlisted()
        .DependsOn(CompileManagedSrc)
        .OnlyWhenStatic(() => IsLinux)
        .Executes(() =>
        {
            var buildDirectory = NativeProfilerProject.Directory / "build";
            EnsureExistingDirectory(buildDirectory);

            CMake.Value(
                arguments: "../ -DCMAKE_BUILD_TYPE=Release",
                workingDirectory: buildDirectory);
            Make.Value(workingDirectory: buildDirectory);

            var source = buildDirectory / "bin" / $"{NativeProfilerProject.Name}.so";
            var dest = TracerHomeDirectory / $"linux-{Platform}";
            CopyFileToDirectory(source, dest, FileExistsPolicy.OverwriteIfNewer);
        });

    Target CompileNativeSrcMacOs => _ => _
        .Unlisted()
        .DependsOn(CompileManagedSrc)
        .OnlyWhenStatic(() => IsOsx)
        .Executes(() =>
        {
            var nativeProjectDirectory = NativeProfilerProject.Directory;
            CMake.Value(arguments: ".", workingDirectory: nativeProjectDirectory);
            Make.Value(workingDirectory: nativeProjectDirectory);
        });

    Target CompileNativeSrc => _ => _
        .Description("Compiles the native loader")
        .DependsOn(CompileNativeSrcWindows)
        .DependsOn(CompileNativeSrcMacOs)
        .DependsOn(CompileNativeSrcLinux);


    Target CompileNativeTestsWindows => _ => _
        .Unlisted()
        .DependsOn(CompileNativeSrcWindows)
        .OnlyWhenStatic(() => IsWin)
        .Executes(() =>
        {
            // If we're building for x64, build for x86 too
            var platforms =
                Equals(Platform, MSBuildTargetPlatform.x64)
                    ? new[] { MSBuildTargetPlatform.x64, MSBuildTargetPlatform.x86 }
                    : new[] { MSBuildTargetPlatform.x86 };

            // Can't use dotnet msbuild, as needs to use the VS version of MSBuild
            MSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .SetTargets("BuildCppTests")
                .DisableRestore()
                .SetMaxCpuCount(null)
                .CombineWith(platforms, (m, platform) => m
                    .SetTargetPlatform(platform)));
        });

    Target CompileNativeTestsLinux => _ => _
        .Unlisted()
        .OnlyWhenStatic(() => IsLinux)
        .Executes(() =>
        {
            Logger.Error("We don't currently run unit tests on Linux");
        });

    Target CompileNativeTests => _ => _
        .Description("Compiles the native loader unit tests")
        .DependsOn(CompileNativeTestsWindows)
        .DependsOn(CompileNativeTestsLinux);

    Target PublishManagedProfiler => _ => _
        .DependsOn(CompileManagedSrc)
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
        .Unlisted()
        .OnlyWhenStatic(() => IsWin)
        .DependsOn(CompileNativeSrcWindows)
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
    
    Target PublishNativeProfilerLinux => _ => _
        .Unlisted()
        .OnlyWhenStatic(() => IsLinux)
        .DependsOn(CompileNativeSrcLinux)
        .Executes(() =>
        {
            // TODO: Linux: x64, arm64; alpine: x64
            foreach (var architecture in new []{ MSBuildTargetPlatform.x64})
            {
                var source = NativeProfilerProject.Directory / "bin" / Configuration / architecture.ToString() /
                             $"{NativeProfilerProject.Name}.so";
                var dest = TracerHomeDirectory / $"linux-{architecture}";
                Logger.Info($"Copying '{source}' to '{dest}'");
                CopyFileToDirectory(source, dest, FileExistsPolicy.OverwriteIfNewer);
            }
        });

    Target PublishNativeProfilerMacOs => _ => _
        .Unlisted()
        .OnlyWhenStatic(() => IsOsx)
        .DependsOn(CompileNativeSrcMacOs)
        .Executes(() =>
        {
            GlobFiles(NativeProfilerProject.Directory / "bin" / $"{NativeProfilerProject.Name}.*")
                .ForEach(source =>
                {
                    var dest = TracerHomeDirectory / $"macos";
                    CopyFileToDirectory(source, dest, FileExistsPolicy.OverwriteIfNewer);

                });
        });

    Target CopyIntegrationsJson => _ => _
        .After(Clean)
        .DependsOn(CreateRequiredDirectories)
        .Executes(() =>
        {
            var source = RootDirectory / "integrations.json";
            var dest = TracerHomeDirectory;

            Logger.Info($"Copying '{source}' to '{dest}'");
            CopyFileToDirectory(source, dest, FileExistsPolicy.OverwriteIfNewer);
        });
    
    Target CompileManagedUnitTests => _ => _
        .DependsOn(Restore)
        .DependsOn(CompileManagedSrc)
        .Executes(() =>
        {
            // Always AnyCPU
            DotNetMSBuild(x => x
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .DisableRestore()
                .SetProperty("BuildProjectReferences", false)
                .SetTargets("BuildCsharpUnitTests"));
        });
    
    Target RunManagedUnitTests => _ => _
        .DependsOn(CompileManagedUnitTests)
        .Executes(() =>
        {
            var testProjects = RootDirectory.GlobFiles("test/**/*.Tests.csproj");

            DotNetTest(x => x
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetDDEnvironmentVariables()
                .CombineWith(testProjects, (x, project) => x
                    .SetProjectFile(project)));
        });

    Target CompileDependencyLibs => _ => _
        .DependsOn(Restore)
        .DependsOn(CompileManagedSrc)
        .Executes(() =>
        {
            // Always AnyCPU
            DotNetMSBuild(x => x
                .SetTargetPath(MsBuildProject)
                .SetConfiguration(Configuration)
                .DisableRestore()
                .EnableNoDependencies()
                .SetTargets("BuildDependencyLibs")
            );
        });

    Target CompileRegressionDependencyLibs => _ => _
        .DependsOn(Restore)
        .DependsOn(CompileManagedSrc)
        .Executes(() =>
        {
            // Platform specific
            DotNetMSBuild(x => x
                .SetTargetPath(MsBuildProject)
                .DisableRestore()
                .EnableNoDependencies()
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetTargets("BuildRegressionDependencyLibs")
            );

            // explicitly build the other dependency (with restore to avoid runtime identifier dependency issues)
            DotNetBuild(x => x
                .SetProjectFile(Solution.GetProject(Projects.ApplicationWithLog4Net))
                // .EnableNoRestore()
                .EnableNoDependencies()
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetNoWarnDotNetCore3()
                .When(!string.IsNullOrEmpty(NugetPackageDirectory), o =>
                    o.SetPackageDirectory(NugetPackageDirectory)));
        });

    Target CompileRegressionSamples => _ => _
        .DependsOn(Restore)
        .DependsOn(CompileRegressionDependencyLibs)
        .Executes(() =>
        {
            var regressionsDirectory = Solution.GetProject(Projects.EntityFramework6xMdTokenLookupFailure)
                .Directory.Parent;
            var regressionLibs = GlobFiles(regressionsDirectory / "**" / "*.csproj")
                .Where(x => !x.Contains("EntityFramework6x.MdTokenLookupFailure")
                            && !x.Contains("ExpenseItDemo")
                            && !x.Contains("StackExchange.Redis.AssemblyConflict.LegacyProject")
                            && !x.Contains("dependency-libs"));

             // Allow restore here, otherwise things go wonky with runtime identifiers
             // in some target frameworks. No, I don't know why
             DotNetBuild(x => x
                 // .EnableNoRestore()
                 .EnableNoDependencies()
                 .SetConfiguration(Configuration)
                 .SetTargetPlatform(Platform)
                 .SetNoWarnDotNetCore3()
                 .When(!string.IsNullOrEmpty(NugetPackageDirectory), o =>
                     o.SetPackageDirectory(NugetPackageDirectory))
                 .CombineWith(regressionLibs, (x, project) => x
                     .SetProjectFile(project)));
        });

    Target CompileFrameworkReproductions => _ => _
        .DependsOn(CompileRegressionDependencyLibs)
        .DependsOn(CompileDependencyLibs)
        .DependsOn(CopyPlatformlessBuildOutput)
        .Executes(() =>
        {
            DotNetMSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .DisableRestore()
                .EnableNoDependencies()
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetTargets("BuildFrameworkReproductions")
                .SetMaxCpuCount(null));
        });
    
    Target CompileIntegrationTests => _ => _
        .DependsOn(CompileManagedSrc)
        .DependsOn(CompileRegressionSamples)
        .DependsOn(CompileFrameworkReproductions)
        .Requires(() => TracerHomeDirectory != null)
        .Executes(() =>
        {
            DotNetMSBuild(s => s
                .SetTargetPath(MsBuildProject)
                .DisableRestore()
                .EnableNoDependencies()
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .SetProperty("ManagedProfilerOutputDirectory", TracerHomeDirectory)
                .SetTargets("BuildCsharpIntegrationTests")
                .SetMaxCpuCount(null));
        });
    
    Target CompileSamples => _ => _
        .DependsOn(CompileDependencyLibs)
        .DependsOn(CopyPlatformlessBuildOutput)
        .DependsOn(CompileFrameworkReproductions)
        .Requires(() => TracerHomeDirectory != null)
        .Executes(() =>
        {
            // This does some "unnecessary" rebuilding and restoring
            var include = RootDirectory.GlobFiles("test/test-applications/integrations/**/*.csproj");
            var exclude = RootDirectory.GlobFiles("test/test-applications/integrations/dependency-libs/**/*.csproj");

            var projects = include.Where(x => !exclude.Contains(x));
            DotNetBuild(config => config
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .EnableNoDependencies()
                .SetProperty("BuildInParallel", "false")
                .SetProperty("ManagedProfilerOutputDirectory", TracerHomeDirectory)
                .SetProperty("ExcludeManagedProfiler", true)
                .SetProperty("ExcludeNativeProfiler", true)
                .SetProperty("LoadManagedProfilerFromProfilerDirectory", false)
                .CombineWith(projects, (s, project) => s
                    .SetProjectFile(project)));
        });

    Target BuildTracerHome => _ => _
        .Description("Builds the tracer home directory from already-compiled sources")
        .DependsOn(CompileManagedSrc)
        .DependsOn(PublishManagedProfiler)
        .DependsOn(PublishNativeProfilerWindows)
        .DependsOn(PublishNativeProfilerLinux)
        .DependsOn(PublishNativeProfilerMacOs)
        .DependsOn(CopyIntegrationsJson);
    
    Target ZipTracerHome => _ => _
        .Unlisted()
        .DependsOn(BuildTracerHome)
        .Executes(() =>
        {
            CompressZip(TracerHomeDirectory, TracerHomeZip, fileMode: FileMode.Create);
        });

    Target BuildMsi => _ => _
        .Description("Builds the .msi files from the compiled tracer home directory")
        .DependsOn(BuildTracerHome)
        .OnlyWhenStatic(() => IsWin)
        .Executes(() =>
        {
            MSBuild(s => s
                    .SetTargetPath(WindowsInstallerProject)
                    .SetConfiguration(Configuration)
                    .AddProperty("RunWixToolsOutOfProc", true)
                    .SetProperty("TracerHomeDirectory", TracerHomeDirectory)
                    .SetMaxCpuCount(null)
                    .CombineWith(ArchitecturesForPlatform, (o, arch) => o
                        .SetProperty("MsiOutputPath", ArtifactsDirectory / arch.ToString())
                        .SetTargetPlatform(arch)),
                degreeOfParallelism: 2);
        });

    Target RunNativeTestsWindows => _ => _
        .Unlisted()
        .DependsOn(CompileNativeSrcWindows)
        .DependsOn(CompileNativeTestsWindows)
        .OnlyWhenStatic(() => IsWin)
        .Executes(() =>
        {
            var workingDirectory = TestsDirectory / "Datadog.Trace.ClrProfiler.Native.Tests" / "bin" / Configuration.ToString() / Platform.ToString();
            var exePath = workingDirectory / "Datadog.Trace.ClrProfiler.Native.Tests.exe";
            var testExe = ToolResolver.GetLocalTool(exePath);
            testExe("--gtest_output=xml", workingDirectory: workingDirectory);
        });

    Target RunNativeTestsLinux => _ => _
        .Unlisted()
        .DependsOn(CompileNativeSrcLinux)
        .DependsOn(CompileNativeTestsLinux)
        .OnlyWhenStatic(() => IsLinux)
        .Executes(() =>
        {
            Logger.Error("We don't currently run unit tests on Linux");
        });

    Target RunNativeTests => _ => _
        .DependsOn(RunNativeTestsWindows)
        .DependsOn(RunNativeTestsLinux);

    Target RunIntegrationTests => _ => _
        .DependsOn(BuildTracerHome)
        .DependsOn(CompileIntegrationTests)
        .DependsOn(CompileSamples)
        .DependsOn(CompileFrameworkReproductions)
        .Executes(() =>
        {
            var projects = new[]
            {
                Solution.GetProject(Projects.TraceIntegrationTests),
                Solution.GetProject(Projects.OpenTracingIntegrationTests),
            };

            DotNetTest(config => config
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .EnableNoRestore()
                .EnableNoBuild()
                .CombineWith(projects, (s, project) => s
                    .SetProjectFile(project)), degreeOfParallelism: 2);

            var clrProfilerIntegrationTests = Solution.GetProject(Projects.ClrProfilerIntegrationTests);

            // TODO: I think we should change this filter to run on Windows by default
            // (RunOnWindows!=False|Category=Smoke)&LoadFromGAC!=True&IIS!=True
            DotNetTest(config => config
                .SetProjectFile(clrProfilerIntegrationTests)
                .SetConfiguration(Configuration)
                .SetTargetPlatform(Platform)
                .EnableNoRestore()
                .EnableNoBuild()
                .SetFilter("(RunOnWindows=True|Category=Smoke)&LoadFromGAC!=True&IIS!=True"));
        });

    /// <summary>
    /// This target is a bit of a hack, but means that we actually use the All CPU builds in intgration tests etc
    /// </summary>
    Target CopyPlatformlessBuildOutput => _ => _
        .Description("Copies the build output from 'All CPU' platforms to platform-specific folders")
        .Unlisted()
        .After(CompileManagedSrc)
        .After(CompileDependencyLibs)
        .After(CompileManagedUnitTests)
        .Executes(() =>
        {
            var directories = RootDirectory.GlobDirectories(
                $"**/bin/bin/src/**/{Configuration}",
                $"**/bin/bin/tools/**/{Configuration}",
                $"**/bin/bin/test/Datadog.Trace.TestHelpers/**/{Configuration}",
                $"**/bin/bin/test/test-applications/integrations/dependency-libs/**/{Configuration}"
            );
            directories.ForEach(source =>
            {
                var target = source.Parent / $"{Platform}" / Configuration;
                if (DirectoryExists(target))
                {
                    Logger.Info($"Skipping '{target}' as already exists");
                }

                CopyDirectoryRecursively(source, target, DirectoryExistsPolicy.Fail, FileExistsPolicy.Fail);
            });
        });

    Target LocalBuild => _ =>
        _
            .Description("Compiles the source and builds the tracer home directory for local usage")
            .DependsOn(CreateRequiredDirectories)
            .DependsOn(RestoreNuGet)
            .DependsOn(CompileManagedSrc)
            .DependsOn(CompileNativeSrc)
            .DependsOn(BuildTracerHome);

    Target WindowsFullCiBuild => _ =>
        _
            .Description("Convenience method for running the same build steps as the full Windows CI build")
            .DependsOn(CreateRequiredDirectories)
            .DependsOn(RestoreNuGet)
            .DependsOn(CompileManagedSrc)
            .DependsOn(CompileNativeSrc)
            .DependsOn(BuildTracerHome)
            .DependsOn(ZipTracerHome)
            .DependsOn(PackNuGet)
            .DependsOn(BuildMsi)
            .DependsOn(CompileManagedUnitTests)
            .DependsOn(CompileDependencyLibs)
            .DependsOn(CopyPlatformlessBuildOutput)
            .DependsOn(CompileRegressionDependencyLibs)
            .DependsOn(CompileFrameworkReproductions)
            .DependsOn(CompileIntegrationTests)
            .DependsOn(CompileSamples)
            .DependsOn(CompileNativeTests);

    Target WindowsCiBuildStage => _ =>
        _
            .Description("Convenience method for running 'build' in the Windows CI build")
            .DependsOn(Clean)
            .DependsOn(CreateRequiredDirectories)
            .DependsOn(RestoreNuGet)
            .DependsOn(CompileManagedSrc)
            .DependsOn(PublishManagedProfiler)
            .DependsOn(CompileNativeSrcWindows)
            .DependsOn(PublishNativeProfilerWindows)
            .DependsOn(CopyIntegrationsJson);

    Target LinuxFullCiBuild => _ =>
        _
            .Description("Convenience method for running the same build steps as the full Windows CI build")
            .DependsOn(Clean)
            .DependsOn(RestoreDotNet)
            .DependsOn(CompileManagedSrc)
            .DependsOn(CompileNativeSrcLinux)
            .DependsOn(BuildTracerHome)
            // .DependsOn(ZipTracerHome)
            .DependsOn(CompileManagedUnitTests)
            .DependsOn(RunManagedUnitTests);

    /// <summary>  
    /// Run the default build 
    /// </summary> 
    public static int Main() => Execute<Build>(x => x.LocalBuild);
}
