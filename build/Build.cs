using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Nuke.Components;

using Serilog;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

[ShutdownDotNetAfterServerBuild]
partial class Build : NukeBuild, ICompile, IPack
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => ((ICompile) x).Compile);

    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    string TagVersion => GitRepository.Tags.SingleOrDefault(x => x.StartsWith('v'))?[1..];

    bool IsTaggedBuild => !string.IsNullOrWhiteSpace(TagVersion);

    string VersionSuffix;

    static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    protected override void OnBuildInitialized()
    {
        VersionSuffix = !IsTaggedBuild
            ? $"preview-{DateTime.UtcNow:yyyyMMdd-HHmm}"
            : "";

        if (IsLocalBuild)
        {
            VersionSuffix = $"dev-{DateTime.UtcNow:yyyyMMdd-HHmm}";
        }

        Log.Information("BUILD SETUP");
        Log.Information("Configuration:\t{Configuration}", ((ICompile) this).Configuration);
        Log.Information("Version suffix:\t{VersionSuffix}", VersionSuffix);
        Log.Information("Tagged build:\t{IsTaggedBuild}", IsTaggedBuild);
    }

    Target Clean => _ => _
        .Before<IRestore>()
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    public Configure<DotNetBuildSettings> CompileSettings => _ => _
        .SetAssemblyVersion(TagVersion)
        .SetFileVersion(TagVersion)
        .SetInformationalVersion(TagVersion)
        .SetVersionSuffix(VersionSuffix);

    Target PublishAot => _ => _
        .After<ICompile>()
        .Executes(() =>
        {
            // also check that publish with trimming doesn't produce errors
            var solution = ((IHazSolution) this).Solution;
            var configuration = ((ICompile) this).Configuration;

            DotNetPublish(s => s
                .SetProject(solution.AllProjects.First(x => x.Name == "Quartz.Examples.Worker"))
                .SetConfiguration(configuration)
            );

            DotNetPublish(s => s
                .SetProject(solution.AllProjects.First(x => x.Name == "Quartz.Examples.AspNetCore"))
                .SetConfiguration(configuration)
            );
        });

    Target UnitTest => _ => _
        .DependsOn<ICompile>()
        .Before<IPack>()
        .Executes(() =>
        {
            var solution = ((IHazSolution) this).Solution;
            var configuration = ((ICompile) this).Configuration;
            var framework = "";
            if (!IsRunningOnWindows)
            {
                framework = "net8.0";
            }

            var testProjects = new[] { "Quartz.Tests.Unit", "Quartz.Tests.AspNetCore" };
            DotNetTest(s => s
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(configuration)
                .SetFramework(framework)
                .SetLoggers(GitHubActions.Instance is not null ? ["GitHubActions"] : Array.Empty<string>())
                .CombineWith(testProjects, (_, testProject) => _
                    .SetProjectFile(solution.GetAllProjects(testProject).First())
                )
            );
        });

    Target IntegrationTest => _ => _
        .DependsOn<ICompile>()
        .Before<IPack>()
        .OnlyWhenDynamic(() => Host is GitHubActions && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        .Executes(() =>
        {
            Action<OutputType,string> logger = (type, output) =>
            {
                if (output.Contains(": NOTICE:", StringComparison.Ordinal))
                {
                    Log.Debug(output);
                }
                else
                {
                    ProcessTasks.DefaultLogger(type, output);
                }
            };

            Log.Information("Starting Postgres");
            ProcessTasks.StartProcess("sudo", "systemctl start postgresql.service").AssertZeroExitCode();
            ProcessTasks.StartProcess("pg_isready").AssertZeroExitCode();

            static void RunAsPostgresUser(string parameters)
            {
                // Warn: Be careful refactoring this to concatenation.
                ProcessTasks.StartProcess("sudo", "-u postgres " + parameters, workingDirectory: Path.GetTempPath()).AssertZeroExitCode();
            }

            Log.Information("Creating user...");
            RunAsPostgresUser("psql --command=\"CREATE USER quartznet PASSWORD 'quartznet'\" --command=\"\\du\"");

            Log.Information("Creating database...");
            RunAsPostgresUser("createdb --owner=quartznet quartznet");

            void RunPsqlAsQuartznetUser(string parameters)
            {
                // Warn: Be careful refactoring this to concatenation
                ProcessTasks.StartProcess(
                    "psql",
                    "--username=quartznet --host=localhost " + parameters,
                    environmentVariables: new Dictionary<string, string> { { "PGPASSWORD", "quartznet" } },
                    logger: logger
                ).AssertZeroExitCode();
            }

            RunPsqlAsQuartznetUser("--list quartznet");

            Log.Information("Creating schema...");
            RunPsqlAsQuartznetUser("-d quartznet -f ./database/tables/tables_postgres.sql");

            var solution = ((IHazSolution) this).Solution;
            var configuration = ((ICompile) this).Configuration;
            var integrationTestProjects = new[] { "Quartz.Tests.Integration" };
            DotNetTest(s => s
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(configuration)
                .SetFramework("net8.0")
                .SetLoggers("GitHubActions")
                .AddProcessAdditionalArguments(" -- NUnit.Where=\"cat !~ firebird and cat !~ oracle and cat !~ mysql and cat !~ sqlserver\"")
                .CombineWith(integrationTestProjects, (_, testProject) => _
                    .SetProjectFile(solution.GetAllProjects(testProject).First())
                )
            );
        });

    public Configure<DotNetPackSettings> PackSettings => _ => _
        .SetAssemblyVersion(TagVersion)
        .SetFileVersion(TagVersion)
        .SetInformationalVersion(TagVersion)
        .SetVersionSuffix(VersionSuffix);

    Target PackZip => _ => _
        .TriggeredBy<IPack>()
        .Produces(((IPack) this).PackagesDirectory / "*.zip")
        .Executes(() =>
        {
            var solution = ((IHazSolution) this).Solution;
            var zipTempDirectory = RootDirectory / "temp" / "package";
            zipTempDirectory.CreateOrCleanDirectory();

            SourceDirectory.Copy(
                target: zipTempDirectory / "src",
                excludeDirectory: dir => dir.Name is "Quartz.Web" or "obj" or "bin",
                excludeFile: file => file.Name.EndsWith(".suo") || file.Name.EndsWith(".user")
            );

            (RootDirectory / "build").Copy(zipTempDirectory / "build", excludeDirectory: dir => dir.Name is "obj" or "bin");

            (RootDirectory / "database").Copy(zipTempDirectory / "database");

            var binaries = solution.Projects
                .Where(x => x.GetProperty("IsPackable") != "false" || x.Name.Contains("Example") || x.Name == "Quartz.Server");

            foreach (var project in binaries)
            {
                (ArtifactsDirectory / "bin" / project.Name).Copy(target: zipTempDirectory / "bin" / project.Name);
            }

            string[] rootFilesToCopy = [
                "Quartz.sln",
                "README.md",
                "build.cmd",
                "build.ps1",
                "build.sh",
                "changelog.md",
                "Directory.Build.props",
                "Directory.Packages.props",
                "license.txt",
                "quartz.net.snk",
            ];
            foreach (var file in rootFilesToCopy)
            {
                (RootDirectory / file).CopyToDirectory(zipTempDirectory);
            }

            var props = File.ReadAllText("Directory.Build.props");
            var baseVersion = Regex.Match(props, "<VersionPrefix>(.+)</VersionPrefix>").Groups[1].Captures[0].Value;

            if (!string.IsNullOrWhiteSpace(VersionSuffix))
            {
                baseVersion += "-";
            }

            ZipFile.CreateFromDirectory(zipTempDirectory, ((IPack) this).PackagesDirectory / $"Quartz.NET-{baseVersion}{VersionSuffix}.zip");
        });
}
