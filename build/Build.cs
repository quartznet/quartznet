using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Fallout.Common;
using Fallout.Common.CI;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Utilities.Collections;
using Fallout.Solutions;

using Serilog;

using static Fallout.Common.Tools.DotNet.DotNetTasks;

[ShutdownDotNetAfterServerBuild]
partial class Build : FalloutBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Database to test against (postgres, sqlserver, mysql, oracle, firebird, sqlite, basic, all)")]
    readonly string Database;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    string TagVersion => GitRepository?.Tags.SingleOrDefault(x => x.StartsWith("v"))?[1..];

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
            DotNetPublish(s => s
                .SetProject(Solution.AllProjects.First(x => x.Name == "Quartz.Examples.AspNetCore"))
                .SetConfiguration(Configuration)
            );
        });

    Target UnitTest => _ => _
        .After(Compile)
        .Executes(() =>
        {
            var framework = "";
            if (!IsRunningOnWindows)
            {
                framework = "net8.0";
            }

            var testProjects = new[] { "Quartz.Tests.Unit", "Quartz.Tests.AspNetCore" };
            DotNetTest(s => s
                .EnableNoRestore()
                .EnableNoBuild()
                .SetConfiguration(Configuration)
                .SetFramework(framework)
                .SetLoggers(GitHubActions.Instance is not null ? ["GitHubActions"] : [])
                .CombineWith(testProjects, (_, testProject) => _
                    .SetProjectFile(Solution.GetAllProjects(testProject).First())
                )
            );
        });

    static readonly string[] DatabaseCategories =
        ["db-postgres", "db-sqlserver", "db-mysql", "db-oracle", "db-firebird", "db-sqlite", "db-redis"];

    string GetTestFilter(string database) => database switch
    {
        "postgres" => "TestCategory=db-postgres",
        "sqlserver" => "TestCategory=db-sqlserver",
        "mysql" => "TestCategory=db-mysql",
        "oracle" => "TestCategory=db-oracle",
        "firebird" => "TestCategory=db-firebird",
        "sqlite" => "TestCategory=db-sqlite",
        "redis" => "TestCategory=db-redis",
        "basic" => string.Join("&", DatabaseCategories.Select(c => $"TestCategory!={c}")),
        _ => null
    };

    Target IntegrationTest => _ => _
        .After(Compile)
        .OnlyWhenDynamic(() => Host is GitHubActions && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        .Executes(() =>
        {
            var database = Database?.ToLowerInvariant();
            Environment.SetEnvironmentVariable("QUARTZ_TEST_DATABASE", database ?? "all");

            var filter = GetTestFilter(database);

            var integrationTestProjects = new[] { "Quartz.Tests.Integration" };
            DotNetTest(s =>
            {
                s = s.EnableNoRestore()
                    .EnableNoBuild()
                    .SetConfiguration(Configuration)
                    .SetFramework("net10.0")
                    .SetLoggers("GitHubActions");

                if (!string.IsNullOrEmpty(filter))
                {
                    s = s.SetFilter(filter);
                }

                return s.CombineWith(integrationTestProjects, (_, testProject) => _
                    .SetProjectFile(Solution.GetAllProjects(testProject).First())
                );
            });
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
                "Quartz.Dashboard",
                "Quartz.Serialization.Json",
                "Quartz.Serialization.SystemTextJson",
                "Quartz.AspNetCore",
                "Quartz.Jobs",
                "Quartz.Plugins",
                "Quartz.Plugins.TimeZoneConverter",
                "Quartz.OpenTelemetry.Instrumentation",
                "Quartz.OpenTracing",
                "Quartz.Extensions.Redis"
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

            SourceDirectory.Copy(
                zipTempDirectory / "src",
                excludeDirectory: dir => dir.Name is "Quartz.Web" or "obj" or "bin",
                excludeFile: file => file.Name.EndsWith(".suo") || file.Name.EndsWith(".user")
            );

            (RootDirectory / "build").Copy(zipTempDirectory / "build", excludeDirectory: dir => dir.Name is "obj" or "bin");

            (RootDirectory / "database").Copy(zipTempDirectory / "database");

            // The bootstrap scripts resolve the Fallout CLI through the local tool manifest.
            (RootDirectory / ".config").Copy(zipTempDirectory / ".config");

            var binaries = Solution.Projects
                .Where(x => x.GetProperty("IsPackable") != "false" || x.Name.Contains("Example") || x.Name == "Quartz.Server");

            foreach (var project in binaries)
            {
                (SourceDirectory / project.Name / "bin" / Configuration).Copy(zipTempDirectory / "bin" / Configuration / project.Name);
            }

            var rootFilesToCopy = new []{"README.md","Quartz.slnx","quartz.net.snk","license.txt", "changelog.md","build.cmd","build.sh","build.ps1"};
            foreach (var file in rootFilesToCopy)
            {
                (RootDirectory / file).CopyToDirectory(zipTempDirectory);
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

            var docsDirectory = ArtifactsDirectory / "apidoc";

            foreach (var file in docsDirectory.GlobFiles("**/*.htm", "**/*.html"))
            {
                var contents = File.ReadAllText(file);
                contents = contents.Replace("@HEADER@", headerContent);
                contents = contents.Replace("@FOOTER@", footerContent);
                File.WriteAllText(file, contents);
            }

            docsDirectory.ZipTo(ArtifactsDirectory / "apidoc-3.0.zip", fileMode: FileMode.Create);
        });
}
