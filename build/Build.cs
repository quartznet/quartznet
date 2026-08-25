using System;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Semver;

using Fallout.Common;
using Fallout.Common.CI;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.Git;
using Fallout.Common.IO;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Utilities.Collections;
using Fallout.Components;
using Fallout.Solutions;

using Serilog;

using static Fallout.Common.Tools.DotNet.DotNetTasks;

[ShutdownDotNetAfterServerBuild]
partial class Build : FalloutBuild, ICompile, IPack
{
    public static int Main() => Execute<Build>(x => ((ICompile) x).Compile);

    [Parameter("Database to test against (postgres, sqlserver, mysql, oracle, firebird, sqlite, basic, all)")]
    readonly string Database;

    [Parameter("Collect line and branch coverage while running the unit tests, in OpenCover format")]
    readonly bool Coverage;

    [GitRepository] readonly GitRepository GitRepository;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath CoverageDirectory => ArtifactsDirectory / "coverage";

    // On GitHub Actions the ref itself is the authority: the host's RefName carries the tag name
    // for both lightweight and annotated tags, where GitRepository.Tags only ever sees lightweight
    // ones (it matches the ref's stored object id against the commit, and an annotated tag's ref
    // points at the tag object). Locally GitRepository is the fallback -- and null in a git
    // worktree, where the local build simply has no tag to version from.
    string TagName =>
        GitHubActions.Instance is { RefType: "tag" } actions
            ? actions.RefName
            : GitRepository?.Tags.FirstOrDefault(x => x.StartsWith('v'));

    // The parsed release version, null when this is not a tagged build. A v-tag that is not valid
    // semantic versioning fails the build in OnBuildInitialized rather than packing a garbage
    // version string.
    SemVersion TagSemVersion =>
        TagName is { } name && name.StartsWith('v')
            ? SemVersion.TryParse(name[1..], SemVersionStyles.Strict, out var parsed) ? parsed : null
            : null;

    bool IsTaggedBuild => TagName is { } name && name.StartsWith('v');

    string VersionPrefix;
    string VersionSuffix;

    string FullVersion => string.IsNullOrWhiteSpace(VersionSuffix) ? VersionPrefix : $"{VersionPrefix}-{VersionSuffix}";

    AbsolutePath VersionPropsFile => RootDirectory / "Directory.Build.props";

    string PropsVersionPrefix =>
        Regex.Match(VersionPropsFile.ReadAllText(), "<VersionPrefix>(.+)</VersionPrefix>", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value;

    static bool IsRunningOnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    protected override void OnBuildInitialized()
    {
        // The tag rules. <VersionPrefix> in Directory.Build.props is only the placeholder that
        // untagged preview builds carry — a stale value there cannot affect a release.
        if (IsTaggedBuild)
        {
            var version = TagSemVersion;
            if (version is null)
            {
                throw new InvalidOperationException(
                    $"Tag '{TagName}' is not valid semantic versioning after the 'v'. " +
                    "A release tag looks like v4.0.0 or v4.0.0-alpha.1; fix the tag rather than the build.");
            }

            VersionPrefix = $"{version.Major}.{version.Minor}.{version.Patch}";
            VersionSuffix = version.IsPrerelease ? version.Prerelease : null;

            if (VersionPrefix != PropsVersionPrefix)
            {
                // Not fatal — the tag wins by design. Surfaced (as a ::warning:: annotation on CI, via
                // Fallout's Serilog sink) so the props file gets caught up after the release.
                Log.Warning("Releasing {FullVersion:l} from tag {TagName:l}, but {File:l} still says {PropsVersion:l} — bump it after the release",
                    FullVersion, TagName, VersionPropsFile.Name, PropsVersionPrefix);
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
        Log.Information("Configuration:\t{Configuration}", ((ICompile) this).Configuration);
        Log.Information("Version:\t{FullVersion}", FullVersion);
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
        .SetAssemblyVersion(VersionPrefix)
        .SetFileVersion(VersionPrefix)
        .SetVersionPrefix(VersionPrefix)
        .SetVersionSuffix(VersionSuffix);

    /// <summary>
    /// Publishes the example applications, one of which is a trim canary, and runs the trim canary that
    /// is not an example.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named <c>PublishAot</c> until issue #3341: nothing here has ever published native AOT, and
    /// leaving the misnomer in place would have collided with the real thing when it arrives.
    /// <c>Quartz.Examples.Worker</c> publishes fully trimmed over Quartz and nothing else, so an IL2xxx
    /// warning it reports is Quartz's own and fails this leg. <c>Quartz.Examples.AspNetCore</c> is a
    /// plain publish, because Razor Pages, MVC and Blazor Server are not trimmable and never will be —
    /// its csproj says so at length.
    /// </para>
    /// <para>
    /// <c>Quartz.Trimming.Canary</c> is published for the runner's own RID and then <em>started</em>, and
    /// a non-zero exit fails the leg. That is step 6's addition, and the reason for it is what step 6
    /// fixed: a persistent job store's serializer threw on the first trigger it wrote in any trimmed
    /// application, and the two warnings that hinted at it were already in the baseline, so a publish
    /// that only compiles could never have found it. The canary asserts that
    /// <c>JsonSerializer.IsReflectionEnabledByDefault</c> really is false and then round-trips every blob
    /// a job store writes through the ordinary serializer. Publishing it for the runner's RID is what
    /// makes it runnable — a trimmed publish is a self-contained one — and it is why this leg gets a
    /// clean output directory rather than reusing an earlier run's.
    /// </para>
    /// <para>
    /// Still no native AOT publish here after step 6, and the reason is a tooling one rather than a
    /// scheduling one: ILCompiler takes no <c>--link-attributes</c>, so <c>ILLink.Suppressions.xml</c> —
    /// which is what makes this leg green without silencing anything for consumers — cannot be handed to
    /// it. An AOT publish of the worker therefore reports every recorded warning as an error, and the
    /// only ways to quiet it are the two the issue has already refused: bake the suppressions into the
    /// shipped assembly, or <c>NoWarn</c> the family and prove nothing. The canary applies no
    /// suppressions at all, so it is the one project here that could carry a native AOT leg; whether it
    /// should is a decision that belongs with <c>IsAotCompatible</c>, and both are still open.
    /// </para>
    /// </remarks>
    Target PublishTrimmed => _ => _
        .After<ICompile>()
        .Executes(() =>
        {
            var solution = ((IHasSolution) this).Solution;
            var configuration = ((ICompile) this).Configuration;

            DotNetPublish(s => s
                .SetProject(solution.AllProjects.First(x => x.Name == "Quartz.Examples.Worker"))
                .SetConfiguration(configuration)
            );

            DotNetPublish(s => s
                .SetProject(solution.AllProjects.First(x => x.Name == "Quartz.Examples.AspNetCore"))
                .SetConfiguration(configuration)
            );

            AbsolutePath canaryDirectory = ArtifactsDirectory / "trim-canary";
            canaryDirectory.CreateOrCleanDirectory();

            DotNetPublish(s => s
                .SetProject(solution.AllProjects.First(x => x.Name == "Quartz.Trimming.Canary"))
                .SetConfiguration(configuration)
                .SetRuntime(RuntimeInformation.RuntimeIdentifier)
                .SetOutput(canaryDirectory)
            );

            AbsolutePath canary = canaryDirectory / (IsRunningOnWindows ? "Quartz.Trimming.Canary.exe" : "Quartz.Trimming.Canary");
            Log.Information("Running the trim canary: {Canary}", canary);

            ProcessTasks.StartProcess(canary, logOutput: true).AssertZeroExitCode();
        });

    /// <summary>
    /// Pairs each named test project with every target framework it declares, rather than forcing one
    /// framework on all of them. <c>dotnet test --framework X</c> against a project that does not target
    /// X exits 0 having run nothing, so a pinned framework quietly becomes "no tests ran" the moment a
    /// project moves — which is how the non-Windows legs ran no unit tests at all once everything moved
    /// to net10.0. net4x needs the .NET Framework runtime, so it is the one framework that cannot run
    /// off Windows.
    /// </summary>
    (Project Project, string Framework)[] GetTestRuns(params string[] projectNames)
    {
        var solution = ((IHasSolution) this).Solution;

        return projectNames
            .Select(x => solution.GetAllProjects(x).First())
            .SelectMany(project => project.GetTargetFrameworks()
                .Where(framework => IsRunningOnWindows || !framework.StartsWith("net4", StringComparison.Ordinal))
                .Select(framework => (Project: project, Framework: framework)))
            .OrderBy(x => x.Project.Name).ThenBy(x => x.Framework)
            .ToArray();
    }

    Target UnitTest => _ => _
        .DependsOn<ICompile>()
        .Before<IPack>()
        .Executes(() =>
        {
            var configuration = ((ICompile) this).Configuration;
            var testRuns = GetTestRuns("Quartz.Tests.Unit", "Quartz.Tests.AspNetCore");

            foreach (var (project, framework) in testRuns)
            {
                Log.Information("Unit tests: {Project} ({Framework})", project.Name, framework);
            }

            if (Coverage)
            {
                CoverageDirectory.CreateOrCleanDirectory();
            }

            DotNetTest(s =>
            {
                s = s.EnableNoRestore()
                    .EnableNoBuild()
                    .SetConfiguration(configuration)
                    .SetLoggers(GitHubActions.Instance is not null ? ["GitHubActions"] : []);

                if (Coverage)
                {
                    // Opt-in, because instrumenting every assembly costs test time that only the Sonar
                    // analysis has a use for — the other workflows run the same target without it. coverlet
                    // writes one <guid>/coverage.opencover.xml per run below the results directory, which is
                    // the layout sonar.cs.opencover.reportsPaths globs for in .github/workflows/sonar.yml.
                    s = s.SetDataCollector("XPlat Code Coverage;Format=opencover")
                        .SetResultsDirectory(CoverageDirectory);
                }

                return s.CombineWith(testRuns, (_, run) => _
                    .SetProjectFile(run.Project)
                    .SetFramework(run.Framework)
                );
            });
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
        .DependsOn<ICompile>()
        .Before<IPack>()
        .OnlyWhenDynamic(() => Host is GitHubActions && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        .Executes(() =>
        {
            var database = Database?.ToLowerInvariant();
            Environment.SetEnvironmentVariable("QUARTZ_TEST_DATABASE", database ?? "all");

            var filter = GetTestFilter(database);

            var configuration = ((ICompile) this).Configuration;
            var testRuns = GetTestRuns("Quartz.Tests.Integration");

            foreach (var (project, framework) in testRuns)
            {
                Log.Information("Integration tests against {Database}: {Project} ({Framework})",
                    database ?? "all", project.Name, framework);
            }

            DotNetTest(s =>
            {
                s = s.EnableNoRestore()
                    .EnableNoBuild()
                    .SetConfiguration(configuration)
                    .SetLoggers("GitHubActions");

                if (!string.IsNullOrEmpty(filter))
                {
                    s = s.SetFilter(filter);
                }

                return s.CombineWith(testRuns, (_, run) => _
                    .SetProjectFile(run.Project)
                    .SetFramework(run.Framework)
                );
            });
        });

    public Configure<DotNetPackSettings> PackSettings => _ => _
        .SetAssemblyVersion(VersionPrefix)
        .SetFileVersion(VersionPrefix)
        .SetVersionPrefix(VersionPrefix)
        .SetVersionSuffix(VersionSuffix);

    Target PackZip => _ => _
        .TriggeredBy<IPack>()
        .Produces(((IPack) this).PackagesDirectory / "*.zip")
        .Executes(() =>
        {
            var solution = ((IHasSolution) this).Solution;
            var zipTempDirectory = RootDirectory / "temp" / "package";
            zipTempDirectory.CreateOrCleanDirectory();

            SourceDirectory.Copy(
                target: zipTempDirectory / "src",
                excludeDirectory: dir => dir.Name is "Quartz.Web" or "obj" or "bin",
                excludeFile: file => file.Name.EndsWith(".suo") || file.Name.EndsWith(".user")
            );

            (RootDirectory / "build").Copy(zipTempDirectory / "build", excludeDirectory: dir => dir.Name is "obj" or "bin");

            (RootDirectory / "database").Copy(zipTempDirectory / "database");

            // The bootstrap scripts resolve the Fallout CLI through the local tool manifest.
            (RootDirectory / ".config").Copy(zipTempDirectory / ".config");

            var binaries = solution.Projects
                .Where(x => x.GetProperty("IsPackable") != "false" || x.Name.Contains("Example") || x.Name == "Quartz.Server");

            foreach (var project in binaries)
            {
                (ArtifactsDirectory / "bin" / project.Name).Copy(target: zipTempDirectory / "bin" / project.Name);
            }

            string[] rootFilesToCopy = [
                "Quartz.slnx",
                "README.md",
                "build.cmd",
                "build.ps1",
                "build.sh",
                "Directory.Build.props",
                "Directory.Packages.props",
                "license.txt",
                "quartz.net.snk",
            ];
            foreach (var file in rootFilesToCopy)
            {
                (RootDirectory / file).CopyToDirectory(zipTempDirectory);
            }

            ZipFile.CreateFromDirectory(zipTempDirectory, ((IPack) this).PackagesDirectory / $"Quartz.NET-{FullVersion}.zip");
        });
}
