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
using Nuke.Common.Tools.Npm;
using Nuke.Common.Utilities.Collections;

using Serilog;

using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.Npm.NpmTasks;

[ShutdownDotNetAfterServerBuild]
partial class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    string TagVersion => GitRepository.Tags.SingleOrDefault(x => x.StartsWith("v"))?[1..];

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
        Log.Information("Configuration:\t{Configuration}", Configuration);
        Log.Information("Version suffix:\t{VersionSuffix}", VersionSuffix);
        Log.Information("Tagged build:\t{IsTaggedBuild}", IsTaggedBuild);
    }

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(x => x.DeleteDirectory());
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .SetDeterministic(IsServerBuild)
                .SetContinuousIntegrationBuild(IsServerBuild)
            );

            // also check that publish with trimming doesn't produce errors
            DotNetPublish(s => s
                .SetProject(Solution.AllProjects.First(x => x.Name == "Quartz.Examples.Worker"))
                .SetConfiguration(Configuration)
            );
        });

    Target DocsBuild => _ => _
        .Executes(() =>
        {
            if (IsServerBuild)
            {
                NpmCi();
            }
            else
            {
                NpmInstall();
            }

            // https://stackoverflow.com/a/69699772/111604
            var nodeVersion = ProcessTasks.StartProcess("node", "--version").AssertWaitForExit().Output.FirstOrDefault().Text.Trim();
            var major = Convert.ToInt32(Regex.Match(nodeVersion, "^v(\\d+)").Groups[1].Captures[0].Value);

            Log.Information("Detected Node.js major version {Version}", major);

            NpmRun(_ => _
                .SetCommand("docs:build")
            );
        });


    Target UnitTest => _ => _
        .After(Compile)
        .Executes(() =>
        {
            var framework = "";
            if (!IsRunningOnWindows)
            {
                framework = "net6.0";
            }

            var testProjects = new[] { "Quartz.Tests.Unit" };
            DotNetTest(s => s
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetFramework(framework)
                .SetLoggers(GitHubActions.Instance is not null ? new [] { "GitHubActions" }  : Array.Empty<string>())
                .CombineWith(testProjects, (_, testProject) => _
                    .SetProjectFile(Solution.GetAllProjects(testProject).First())
                )
            );
        });

    Target IntegrationTest => _ => _
        .After(Compile)
        .OnlyWhenDynamic(() => Host is GitHubActions && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        .Executes(() =>
        {
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

            static void RunPsqlAsQuartznetUser(string parameters)
            {
                // Warn: Be careful refactoring this to concatenation
                ProcessTasks.StartProcess("psql", $"--username=quartznet --host=localhost " + parameters, environmentVariables: new Dictionary<string, string> { { "PGPASSWORD", "quartznet" } }).AssertZeroExitCode();
            }

            RunPsqlAsQuartznetUser("--list quartznet");

            Log.Information("Creating schema...");
            RunPsqlAsQuartznetUser("-d quartznet -f ./database/tables/tables_postgres.sql");

            var integrationTestProjects = new[] { "Quartz.Tests.Integration" };
            DotNetTest(s => s
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetFramework("net6.0")
                .SetLoggers("GitHubActions")
                .SetFilter("TestCategory!=db-firebird&TestCategory!=db-oracle&TestCategory!=db-mysql&TestCategory!=db-sqlserver")
                .CombineWith(integrationTestProjects, (_, testProject) => _
                    .SetProjectFile(Solution.GetAllProjects(testProject).First())
                )
            );
        });

    Target Pack => _ => _
        .After(Compile, UnitTest)
        .Produces(ArtifactsDirectory / "*.*")
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();

            var packTargetProjects = new[]
            {
                "Quartz",
                "Quartz.Extensions.DependencyInjection",
                "Quartz.Extensions.Hosting",
                "Quartz.Serialization.Json",
                "Quartz.AspNetCore",
                "Quartz.Jobs",
                "Quartz.Plugins",
                "Quartz.Plugins.TimeZoneConverter",
                "Quartz.OpenTelemetry.Instrumentation",
                "Quartz.OpenTracing"
            };

            foreach (var project in packTargetProjects)
            {
                DotNetPack(s => s
                    .SetProject(Solution.GetProject(project))
                    .SetAssemblyVersion(TagVersion)
                    .SetFileVersion(TagVersion)
                    .SetInformationalVersion(TagVersion)
                    .SetVersionSuffix(VersionSuffix)
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(ArtifactsDirectory)
                    .SetDeterministic(IsServerBuild)
                    .SetContinuousIntegrationBuild(IsServerBuild)
                );
            }

            var zipContents = Array.Empty<AbsolutePath>()
                    .Concat(SourceDirectory.GlobFiles("**/*.*"))
                    .Concat(RootDirectory.GlobFiles("database/**/*"))
                    .Concat(RootDirectory.GlobFiles("changelog.md"))
                    .Concat(RootDirectory.GlobFiles("license.txt"))
                    .Concat(RootDirectory.GlobFiles("README.md"))
                    .Concat(RootDirectory.GlobFiles("*.sln"))
                    .Concat(RootDirectory.GlobFiles("build.*"))
                    .Concat(RootDirectory.GlobFiles("quartz.net.snk"))
                    .Concat(RootDirectory.GlobFiles("build.*"))
                    .Where(x => !x.Contains(""))
                    .Where(x => !x.Contains("Quartz.Web"))
                    .Where(x => !x.Contains("Quartz.Benchmark"))
                    .Where(x => !x.Contains("Quartz.Test"))
                    .Where(x => !x.Contains("/obj/"))
                    .Where(x => !x.ToString().EndsWith(".suo"))
                    .Where(x => !x.ToString().EndsWith(".user"))
                ;

            var zipTempDirectory = RootDirectory / "temp" / "package";
            zipTempDirectory.CreateOrCleanDirectory();

            CopyDirectoryRecursively(
                source: SourceDirectory,
                target: zipTempDirectory / "src",
                excludeDirectory: dir => dir.Name is "Quartz.Web" or "obj" or "bin",
                excludeFile: file => file.Name.EndsWith(".suo") || file.Name.EndsWith(".user"));

            CopyDirectoryRecursively(
                source: RootDirectory / "build",
                target: zipTempDirectory / "build",
                excludeDirectory: dir => dir.Name is "obj" or "bin");

            CopyDirectoryRecursively(source: RootDirectory / "database", target: zipTempDirectory / "database");

            var binaries = Solution.Projects
                .Where(x => x.GetProperty("IsPackable") != "false" || x.Name.Contains("Example") || x.Name == "Quartz.Server");

            foreach (var project in binaries)
            {
                CopyDirectoryRecursively(source: SourceDirectory / project.Name / "bin" / Configuration, target: zipTempDirectory / "bin" / Configuration / project.Name);
            }
            
            var rootFilesToCopy = new []{"README.md","Quartz.sln","quartz.net.snk","license.txt", "changelog.md","build.cmd","build.sh","build.ps1"};
            foreach (var file in rootFilesToCopy)
            {
                CopyFileToDirectory(RootDirectory / file, zipTempDirectory);
            }

            var props = File.ReadAllText(SourceDirectory / "Directory.Build.props");
            var baseVersion = Regex.Match(props, "<VersionPrefix>(.+)</VersionPrefix>").Groups[1].Captures[0].Value;

            if (!string.IsNullOrWhiteSpace(VersionSuffix))
            {
                baseVersion += "-";
            }

            ZipFile.CreateFromDirectory(zipTempDirectory, ArtifactsDirectory / $"Quartz.NET-{baseVersion}{VersionSuffix}.zip");
        });

    Target ApiDoc => _ => _
        .Executes(() =>
        {
            var headerContent = File.ReadAllText("doc/header.template");
            var footerContent = File.ReadAllText("doc/footer.template");

            var docsDirectory = RootDirectory / "build" / "apidoc";

            foreach (var file in docsDirectory.GlobFiles("**/*.htm", "**/*.html"))
            {
                var contents = File.ReadAllText(file);
                contents = contents.Replace("@HEADER@", headerContent);
                contents = contents.Replace("@FOOTER@", footerContent);
                File.WriteAllText(file, contents);
            }
        });
}
