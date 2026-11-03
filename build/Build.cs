using System;
using System.Collections.Generic;
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
using Fallout.Common.Utilities;
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

    [Parameter("Major version of a newer .NET runtime the unit test hosts roll forward to, such as 11, while the build stays on the SDK global.json names. The run fails unless every host reports that runtime")]
    readonly int? UnitTestRuntime;

    [Solution] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    // On GitHub Actions the ref itself is the authority: the host's RefName carries the tag name
    // for both lightweight and annotated tags, where GitRepository.Tags only ever sees lightweight
    // ones (it matches the ref's stored object id against the commit, and an annotated tag's ref
    // points at the tag object). Locally GitRepository is the fallback -- and null in a git
    // worktree, where the local build simply has no tag to version from.
    string TagName =>
        GitHubActions.Instance is { RefType: "tag" } actions
            ? actions.RefName
            : GitRepository?.Tags.FirstOrDefault(x => x.StartsWith('v'));

    string TagVersion => TagName is { } name && name.StartsWith('v') ? name[1..] : null;

    bool IsTaggedBuild => !string.IsNullOrWhiteSpace(TagVersion);

    string VersionPrefix;
    string VersionSuffix;

    string FullVersion => string.IsNullOrWhiteSpace(VersionSuffix) ? VersionPrefix : $"{VersionPrefix}-{VersionSuffix}";

    AbsolutePath VersionPropsFile => SourceDirectory / "Directory.Build.props";

    string PropsVersionPrefix =>
        Regex.Match(VersionPropsFile.ReadAllText(), "<VersionPrefix>(.+)</VersionPrefix>").Groups[1].Value;

    static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    protected override void OnBuildInitialized()
    {
        // The tag rules. <VersionPrefix> in src/Directory.Build.props is only the placeholder that
        // untagged preview builds carry — a stale value there cannot affect a release.
        if (IsTaggedBuild)
        {
            var separator = TagVersion.IndexOf('-');
            VersionPrefix = separator < 0 ? TagVersion : TagVersion[..separator];
            VersionSuffix = separator < 0 ? null : TagVersion[(separator + 1)..];

            if (VersionPrefix != PropsVersionPrefix)
            {
                // Not fatal — the tag wins by design. Surfaced (as a ::warning:: annotation on CI, via
                // Fallout's Serilog sink) so the props file gets caught up after the release.
                Log.Warning("Releasing {FullVersion:l} from tag v{TagVersion:l}, but {File:l} still says {PropsVersion:l} — bump it after the release",
                    FullVersion, TagVersion, VersionPropsFile.Name, PropsVersionPrefix);
            }
        }
        else
        {
            VersionPrefix = PropsVersionPrefix;
            VersionSuffix = $"preview-{DateTime.UtcNow:yyyyMMdd-HHmm}";
        }

        if (IsLocalBuild)
        {
            VersionSuffix = $"dev-{DateTime.UtcNow:yyyyMMdd-HHmm}";
        }

        Log.Information("BUILD SETUP");
        Log.Information("Configuration:\t{Configuration}", Configuration);
        Log.Information("Version:\t{FullVersion}", FullVersion);
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
            var testProjects = new[] { "Quartz.Tests.Unit", "Quartz.Tests.AspNetCore" };

            // Run each project once per target framework it declares, rather than forcing one
            // framework on all of them. `dotnet test --framework X` against a project that does not
            // target X exits 0 having run nothing, so a single shared value silently skipped whole
            // projects instead of failing loudly. net4x needs the .NET Framework runtime, so it is
            // the one framework that cannot run off Windows, and the one a runtime leg leaves out: a
            // .NET Framework host never rolls forward onto .NET.
            var testRuns = testProjects
                .Select(x => Solution.GetAllProjects(x).First())
                .SelectMany(project => project.GetTargetFrameworks()
                    .Where(framework => (IsRunningOnWindows && UnitTestRuntime is null) || !framework.StartsWith("net4", StringComparison.Ordinal))
                    .Select(framework => (Project: project, Framework: framework)))
                .OrderBy(x => x.Project.Name).ThenBy(x => x.Framework)
                .ToArray();

            foreach (var (project, framework) in testRuns)
            {
                Log.Information("Unit tests: {Project} ({Framework})", project.Name, framework);
            }

            // A runtime leg's hosts only: the variables reach the test hosts through dotnet test's -e, so
            // dotnet test itself and MSBuild stay on the runtime the SDK names. Each run writes a VSTest
            // diagnostics log, whose host log says where the host's runtime was loaded from.
            AbsolutePath diagnostics = ArtifactsDirectory / "test-diagnostics";
            if (UnitTestRuntime is not null)
            {
                diagnostics.CreateOrCleanDirectory();
            }

            bool passed = false;
            try
            {
                DotNetTest(s => s
                    .EnableNoRestore()
                    .EnableNoBuild()
                    .SetConfiguration(Configuration)
                    .SetLoggers(GitHubActions.Instance is not null ? ["GitHubActions"] : [])
                    .When(UnitTestRuntime is not null, _ => _
                        .SetProcessAdditionalArguments([.. RollForwardVariables.SelectMany(x => new[] { "-e", $"{x.Key}={x.Value}" })]))
                    .CombineWith(testRuns, (_, run) => _
                        .SetProjectFile(run.Project)
                        .SetFramework(run.Framework)
                        .When(UnitTestRuntime is not null, _ => _
                            .SetDiagnosticsFile(diagnostics / $"{run.Project.Name}.{run.Framework}.txt"))
                    ),
                    // On a runtime leg every project runs even when one fails: each failure is a finding
                    // about the newer runtime, and one run should report all of them.
                    completeOnFailure: UnitTestRuntime is not null
                );

                passed = true;
            }
            finally
            {
                // Said whether the tests passed or not, so that a red leg still says which runtime it was
                // red on; held against the runtime only when nothing else failed first.
                if (UnitTestRuntime is { } major)
                {
                    AssertTheTestHostsRanOn(major, testRuns, diagnostics, assert: passed);
                }
            }
        });

    /// <summary>
    /// What a test host is handed to run on the newest runtime installed rather than the one it was built
    /// for: <see cref="UnitTestRuntime" />'s leg.
    /// </summary>
    /// <remarks>
    /// <c>LatestMajor</c> rather than <c>Major</c>, which rolls forward only when the runtime the host was
    /// built for is missing, and on that leg it is installed beside the newer one. The prerelease switch is
    /// what lets a release candidate be chosen at all.
    /// </remarks>
    static readonly IReadOnlyDictionary<string, string> RollForwardVariables = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DOTNET_ROLL_FORWARD"] = "LatestMajor",
        ["DOTNET_ROLL_FORWARD_TO_PRERELEASE"] = "1",
    };

    /// <summary>
    /// Fails unless every test host of every one of <paramref name="testRuns" /> loaded .NET
    /// <paramref name="major" />, read from the <c>Runtime location</c> line each host writes into its
    /// VSTest diagnostics log, which ends in the version: <c>.../Microsoft.NETCore.App/11.0.0-rc.3.26522.148</c>.
    /// </summary>
    /// <remarks>
    /// The check that the leg ran what it says it ran. Without it, a runtime that went missing from the
    /// runner, or variables that stopped reaching the hosts, would turn the leg into a second run on the
    /// runtime the other legs already cover, and it would still pass. With <paramref name="assert" />
    /// false, after a run that has already failed, it only says what it found.
    /// </remarks>
    static void AssertTheTestHostsRanOn(int major, (Project Project, string Framework)[] testRuns, AbsolutePath diagnostics, bool assert)
    {
        List<string> problems = [];

        foreach (var (project, framework) in testRuns)
        {
            string prefix = $"{project.Name}.{framework}.host.";
            List<string> versions = diagnostics.GlobFiles($"{prefix}*")
                .SelectMany(x => x.ReadAllLines())
                .Select(x => Regex.Match(x, @"Runtime location: .*Microsoft\.NETCore\.App[\\/](?<version>[^\\/\s]+)\s*$"))
                .Where(x => x.Success)
                .Select(x => x.Groups["version"].Value)
                .Distinct()
                .ToList();

            if (versions.Count == 0)
            {
                problems.Add($"{project.Name} ({framework}) left no host log saying where its runtime was loaded from");
                continue;
            }

            foreach (string version in versions)
            {
                Log.Information("{Project} ({Framework}) ran on .NET {Version}", project.Name, framework, version);

                if (!version.StartsWith($"{major}.", StringComparison.Ordinal))
                {
                    problems.Add($"{project.Name} ({framework}) ran on .NET {version}");
                }
            }
        }

        string message = $"Every unit test host was to run on .NET {major}, with {string.Join(" and ", RollForwardVariables.Select(x => $"{x.Key}={x.Value}"))}:"
            + Environment.NewLine + string.Join(Environment.NewLine, problems);

        if (!assert)
        {
            if (problems.Count > 0)
            {
                Log.Warning(message);
            }

            return;
        }

        Assert.True(problems.Count == 0, message);
    }

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
                    .SetAssemblyVersion(VersionPrefix)
                    .SetFileVersion(VersionPrefix)
                    .SetVersionPrefix(VersionPrefix)
                    .SetVersionSuffix(VersionSuffix)
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(ArtifactsDirectory)
                    .SetDeterministic(IsServerBuild)
                    .SetContinuousIntegrationBuild(IsServerBuild)
                );
            }
        });

    /// <summary>
    /// The source-and-binaries archive a release carries. A target of its own, triggered by
    /// <see cref="Pack"/>, as it is on main: <c>DraftRelease</c> orders itself after it by this name,
    /// and <c>build/Build.Release.cs</c> stays byte-identical on both branches because of it.
    /// </summary>
    Target PackZip => _ => _
        .TriggeredBy(Pack)
        .Produces(ArtifactsDirectory / "*.zip")
        .Executes(() =>
        {
            var zipContents = Array.Empty<AbsolutePath>()
                    .Concat(SourceDirectory.GlobFiles("**/*.*"))
                    .Concat(RootDirectory.GlobFiles("database/**/*"))
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

            var rootFilesToCopy = new []{"README.md","Quartz.slnx","quartz.net.snk","license.txt","build.cmd","build.sh","build.ps1"};
            foreach (var file in rootFilesToCopy)
            {
                (RootDirectory / file).CopyToDirectory(zipTempDirectory);
            }

            ZipFile.CreateFromDirectory(zipTempDirectory, ArtifactsDirectory / $"Quartz.NET-{FullVersion}.zip");
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
