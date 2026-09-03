using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

    [Parameter("Database to test against (postgres, sqlserver, mysql, oracle, firebird, sqlite, redis, basic, all)")]
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

    /// <summary>
    /// Whether this build is the one whose output is published, which is every build on GitHub Actions.
    /// </summary>
    /// <remarks>
    /// It sets <c>ContinuousIntegrationBuild</c>, which is what makes the SDK normalize the source paths
    /// embedded in a PDB into <c>/_/</c>-rooted ones SourceLink can resolve. Without it a shipped symbol
    /// package carries the runner's absolute paths, a debugger can only step into sources on a machine
    /// laid out the same way, and no two builds of the same commit produce the same bytes. Off locally,
    /// deliberately: a developer debugging their own build wants the paths on their own disk.
    /// </remarks>
    static bool IsContinuousIntegrationBuild => GitHubActions.Instance is not null;

    public Configure<DotNetBuildSettings> CompileSettings => _ => _
        .SetAssemblyVersion(VersionPrefix)
        .SetFileVersion(VersionPrefix)
        .SetVersionPrefix(VersionPrefix)
        .SetVersionSuffix(VersionSuffix)
        .SetContinuousIntegrationBuild(IsContinuousIntegrationBuild);

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
    /// The canary's own publish does not stop at the first recorded warning, and this leg checks the
    /// warnings it reported instead — see <see cref="AssertOnlyRecordedTrimWarnings" />. Since the canary
    /// grew a scheduler it reaches the reflection the baseline records, and a publish that fails on the
    /// first line of it is a canary that never runs. The worker is unchanged: it applies
    /// <c>ILLink.Suppressions.xml</c> the SDK's own way and still fails on anything outside it.
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

            var output = DotNetPublish(s => s
                .SetProject(solution.AllProjects.First(x => x.Name == "Quartz.Trimming.Canary"))
                .SetConfiguration(configuration)
                .SetRuntime(RuntimeInformation.RuntimeIdentifier)
                .SetOutput(canaryDirectory)
            );

            AssertOnlyRecordedTrimWarnings(output, "the trimmed canary publish");
            RunCanary(canaryDirectory, "trim canary");
        });

    /// <summary>
    /// Publishes the canary as a native executable for the runner's own architecture and runs it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The artefact issue #3341 spent seven steps working towards. A native AOT publish is the only
    /// build that has no runtime code generation to fall back on and no assemblies left to reflect over,
    /// so a Quartz that runs a persistent job store out of one has been proven rather than argued: the
    /// canary creates a SQLite database, schedules a job, waits to be told it fired, and reads the job
    /// and the trigger back.
    /// </para>
    /// <para>
    /// ILCompiler has no link-attributes option — a fact recorded on step 5 of the issue and rechecked
    /// against <c>ilc --help</c> here — so <c>ILLink.Suppressions.xml</c> cannot be handed to it, and
    /// the recorded reflection would otherwise fail this publish on its first line. Rather than bake the
    /// suppressions into the shipped assembly or <c>NoWarn</c> the family and prove nothing — the two
    /// answers the issue has already refused — the canary lets ILCompiler report, and this target reads
    /// the report and applies the baseline itself.
    /// </para>
    /// </remarks>
    Target PublishAot => _ => _
        .After<ICompile>()
        .Executes(() =>
        {
            var solution = ((IHasSolution) this).Solution;
            var configuration = ((ICompile) this).Configuration;

            AbsolutePath canaryDirectory = ArtifactsDirectory / "aot-canary";
            canaryDirectory.CreateOrCleanDirectory();

            var output = DotNetPublish(s => s
                .SetProject(solution.AllProjects.First(x => x.Name == "Quartz.Trimming.Canary"))
                .SetConfiguration(configuration)
                .SetRuntime(RuntimeInformation.RuntimeIdentifier)
                .SetProperty("PublishAot", true)
                .SetOutput(canaryDirectory)
            );

            AssertOnlyRecordedTrimWarnings(output, "the native AOT canary publish");
            RunCanary(canaryDirectory, "native AOT canary");
        });

    void RunCanary(AbsolutePath canaryDirectory, string what)
    {
        AbsolutePath canary = canaryDirectory / (IsRunningOnWindows ? "Quartz.Trimming.Canary.exe" : "Quartz.Trimming.Canary");
        Log.Information("Running the {What}: {Canary}", what, canary);

        ProcessTasks.StartProcess(canary, logOutput: true).AssertZeroExitCode();
    }

    /// <summary>
    /// Fails when a publish reported a trim or AOT warning against a Quartz type that
    /// <c>src/Quartz/ILLink.Suppressions.xml</c> does not record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same baseline the worker's trimmed publish is given through the SDK, applied here by reading
    /// it — which is the only way ILCompiler can be told, and, since the two tools have to agree, the way
    /// both canary publishes are checked. A warning naming a type the file lists with that warning code
    /// is expected and logged; a warning naming any other Quartz type fails the leg, which is what makes
    /// this a baseline rather than a silence.
    /// </para>
    /// <para>
    /// Warnings from assemblies that are not Quartz's are logged and left alone: what a driver package
    /// says about its own trimmability is not this repository's to record, and the canary references one
    /// on purpose.
    /// </para>
    /// </remarks>
    void AssertOnlyRecordedTrimWarnings(IReadOnlyCollection<Output> output, string what)
    {
        var recorded = ReadTrimBaseline();
        var unrecorded = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in output.Select(x => x.Text))
        {
            var match = Regex.Match(line, @"\b(IL\d{4})\b: ([^:(\s]+)", RegexOptions.None, TimeSpan.FromSeconds(5));
            if (!match.Success || !seen.Add(match.Value))
            {
                continue;
            }

            string code = match.Groups[1].Value;
            string member = match.Groups[2].Value;

            // "Assembly 'Quartz' produced trim warnings" - the one warning that names no member,
            // reported when a whole assembly's warnings are collapsed into a single line. Nothing below
            // could check what it stands for, so it fails here rather than passing as somebody else's.
            if (code == "IL2104" && line.Contains("'Quartz'", StringComparison.Ordinal))
            {
                unrecorded.Add($"{code}: Quartz's warnings were collapsed into one line, so none of them was checked");
                continue;
            }

            if (!member.StartsWith("Quartz.", StringComparison.Ordinal))
            {
                Log.Information("{What} reported {Code} against {Member}, which is not Quartz's to record", what, code, member);
                continue;
            }

            if (recorded.Any(entry => entry.Code == code && IsWithin(member, entry.Type)))
            {
                Log.Debug("{What} reported the recorded {Code} against {Member}", what, code, member);
                continue;
            }

            unrecorded.Add($"{code}: {member}");
        }

        if (unrecorded.Count > 0)
        {
            Assert.Fail(
                $"{what} reported {unrecorded.Count} trim warning(s) that src/Quartz/ILLink.Suppressions.xml does not record:"
                + Environment.NewLine + string.Join(Environment.NewLine, unrecorded.Order(StringComparer.Ordinal))
                + Environment.NewLine
                + "Fix the reflection, or make the case for a new entry - see the header of src/Quartz/TrimAnalysisBaseline.cs.");
        }

        Log.Information("{What} reported nothing outside the recorded baseline", what);
    }

    /// <summary>
    /// The (type, warning code) pairs recorded in the ILLink baseline, with the trailing <c>*</c> each
    /// entry carries for compiler-generated companions stripped.
    /// </summary>
    IReadOnlyList<(string Type, string Code)> ReadTrimBaseline()
    {
        AbsolutePath baseline = SourceDirectory / "Quartz" / "ILLink.Suppressions.xml";
        var recorded = XDocument.Load(baseline)
            .Descendants("type")
            .SelectMany(type => type.Descendants("attribute")
                .Select(attribute => (
                    Type: type.Attribute("fullname")!.Value.TrimEnd('*'),
                    Code: attribute.Elements("argument").Skip(1).First().Value)))
            .ToList();

        Assert.NotEmpty(recorded, $"{baseline} records no trim warnings at all, so this check would pass anything");
        return recorded;
    }

    /// <summary>
    /// Whether a warning's member belongs to a recorded type — the type itself, or one of the closure
    /// and state-machine types the compiler nests inside it.
    /// </summary>
    static bool IsWithin(string member, string type) =>
        member.Length > type.Length
        && member.StartsWith(type, StringComparison.Ordinal)
        && member[type.Length] is '.' or '`' or '+' or '/' or '<';

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

    /// <summary>
    /// Executes every benchmark once and fails the leg when one of them does not run to completion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Quartz.Benchmark</c> is in the solution, so <c>Compile</c> has always caught a benchmark that
    /// stopped compiling; nothing caught one that compiled and threw. Issue #3439 is that gap, and the
    /// first run of this target found it: every case in <c>SchedulerBenchmark</c> had been failing on a
    /// scheduler it could no longer configure. This is a liveness check on the harness — nothing is
    /// measured, published or compared, and no build fails over a number.
    /// </para>
    /// <para>
    /// What the run covers, and the two categories it leaves out, is decided in the benchmark project
    /// behind <c>--smoke</c> rather than by a list of names here: a benchmark written later is in it
    /// without anybody remembering to add it, which is the property that makes this worth running.
    /// <c>src/Quartz.Benchmark/Program.cs</c> and <c>BenchmarkCategories</c> say the rest.
    /// </para>
    /// <para>
    /// Release whatever the rest of the build is configured as, because BenchmarkDotNet refuses a
    /// non-optimized assembly and a smoke run of one would prove nothing. It runs after the unit tests
    /// rather than beside them: both want the machine, and the tests are the ones whose failure is worth
    /// reading first.
    /// </para>
    /// </remarks>
    Target BenchmarkSmoke => _ => _
        .DependsOn<ICompile>()
        .After(UnitTest)
        .Before<IPack>()
        .Executes(() =>
        {
            var solution = ((IHasSolution) this).Solution;

            DotNetRun(s => s
                .SetProjectFile(solution.AllProjects.First(x => x.Name == "Quartz.Benchmark"))
                .SetConfiguration(Configuration.Release)
                .SetApplicationArguments("--smoke")
            );
        });

    /// <summary>
    /// Runs the Wolverine example end to end, so a recipe that compiles is also a recipe that works.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Quartz.Examples.Wolverine</c> is in the solution, so <c>Compile</c> catches the calls it makes
    /// going away. What compiling cannot catch is the part of the integration that is about two runtimes
    /// agreeing: hosted-service ordering, a scheduler started by something other than its own hosted
    /// service, a trigger group used as a correlation axis, and a serialized envelope handed back to a
    /// message bus. <c>--smoke</c> asserts each of those produced its effect and returns non-zero when
    /// one did not; <c>src/Quartz.Examples.Wolverine/Smoke.cs</c> is the list.
    /// </para>
    /// <para>
    /// Nothing external is needed — Wolverine's in-memory local transport and Quartz's in-memory store.
    /// The example's Postgres mode is reached through an environment variable and is deliberately not
    /// exercised here; it needs a database, which is what the <c>pr-integration-*</c> workflows are for.
    /// </para>
    /// <para>
    /// Beside <see cref="BenchmarkSmoke" /> and after the unit tests, for the same reason: both want the
    /// machine, and a failing test is the one worth reading first.
    /// </para>
    /// </remarks>
    Target WolverineSmoke => _ => _
        .DependsOn<ICompile>()
        .After(UnitTest)
        .Before<IPack>()
        .Executes(() =>
        {
            var solution = ((IHasSolution) this).Solution;
            var configuration = ((ICompile) this).Configuration;

            DotNetRun(s => s
                .SetProjectFile(solution.AllProjects.First(x => x.Name == "Quartz.Examples.Wolverine"))
                .SetConfiguration(configuration)
                .SetApplicationArguments("--smoke")
            );
        });

    /// <summary>
    /// Starts each example application and waits for it to say it is doing what it exists to show.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PublishTrimmed" /> publishes <c>Quartz.Examples.Worker</c> and
    /// <c>Quartz.Examples.AspNetCore</c> and has never started either, so an example that compiles and
    /// publishes but cannot get through <c>Build().Run()</c> passed every leg there is. Both were broken
    /// at beta.1 — one by a pair of scheduling options that refuse each other at startup, the other by a
    /// job asking for an <c>IHttpClientFactory</c> nothing registered, which Development's registration
    /// validation turns into a refusal to start — and nothing in CI could have noticed either. This is
    /// what would have.
    /// </para>
    /// <para>
    /// Development is the environment on purpose: it is the one the examples are read and run in, and the
    /// one that validates the whole container at build time rather than discovering a missing
    /// registration on first use. Each application is started from its build output rather than through
    /// <c>dotnet run</c>, so the process this target holds is the application itself — killing
    /// <c>dotnet run</c> would leave a web server listening on the runner.
    /// </para>
    /// <para>
    /// The third example is the interactive tour, whose <c>--list</c> is the part of it a machine can
    /// run; it walks the catalogue every entry is registered in and exits.
    /// </para>
    /// <para>
    /// Beside <see cref="BenchmarkSmoke" /> and <see cref="WolverineSmoke" />, after the unit tests, for
    /// the reason those give: all of them want the machine, and a failing test is the one worth reading
    /// first.
    /// </para>
    /// </remarks>
    Target ExamplesSmoke => _ => _
        .DependsOn<ICompile>()
        .After(UnitTest)
        .Before<IPack>()
        .Executes(() =>
        {
            var solution = ((IHasSolution) this).Solution;
            var configuration = ((ICompile) this).Configuration;

            DotNetRun(s => s
                .SetProjectFile(solution.AllProjects.First(x => x.Name == "Quartz.Examples"))
                .SetConfiguration(configuration)
                .SetApplicationArguments("--list")
            );

            RunExampleUntilItSays(
                "Quartz.Examples.Worker",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["DOTNET_ENVIRONMENT"] = "Development" },
                // The host is up, and the job the example schedules ran: the hosted service holds the
                // scheduler back for ten seconds before the first trigger can fire, so the second line
                // is the one that says the whole of it worked.
                ["Application started", "job executing, triggered by"]);

            RunExampleUntilItSays(
                "Quartz.Examples.AspNetCore",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    // Kestrel's default endpoints are 5000 and an HTTPS 5001 that wants a development
                    // certificate no runner has. Naming one loopback port answers both, and lets two of
                    // these run at once without colliding. The example's launch profile also says 5000,
                    // and is not read here: launchSettings.json belongs to 'dotnet run'.
                    ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{FreeLoopbackPort()}",
                },
                ["Application started"]);
        });

    /// <summary>
    /// How long an example is given to say everything it was started to say.
    /// </summary>
    /// <remarks>
    /// Generous rather than tight, because what the bound is for is a hang and a hang does not end: the
    /// worker's hosted service waits ten seconds before starting its scheduler, and a cold runner spends
    /// a while on the first JIT of an ASP.NET Core pipeline. Nothing here is a measurement.
    /// </remarks>
    static readonly TimeSpan ExampleStartTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Starts one example, waits for every line it was started to produce, and stops it. A non-zero exit,
    /// an exit at all, or a line that never arrives fails the target.
    /// </summary>
    void RunExampleUntilItSays(string projectName, IReadOnlyDictionary<string, string> environment, IReadOnlyList<string> markers)
    {
        var configuration = ((ICompile) this).Configuration;

        // Where UseArtifactsOutput in Directory.Build.props puts a build. Asserted rather than assumed,
        // so a layout that moves says so by name instead of failing as a process that would not start.
        AbsolutePath assembly = ArtifactsDirectory / "bin" / projectName / configuration.ToString().ToLowerInvariant() / $"{projectName}.dll";
        Assert.FileExists(assembly);

        ProcessStartInfo startInfo = new()
        {
            FileName = DotNetPath,
            // The content root a host takes when it is not told one, which is where the example's
            // appsettings.json and its XML schedule were copied to.
            WorkingDirectory = assembly.Parent,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add(assembly);

        foreach (KeyValuePair<string, string> variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        List<string> output = [];
        HashSet<string> waiting = new(markers, StringComparer.Ordinal);

        using Process process = new() { StartInfo = startInfo };

        // One lock over both, because the reader threads write them and this thread reads them.
        void Received(object sender, DataReceivedEventArgs line)
        {
            if (line.Data is null)
            {
                return;
            }

            lock (output)
            {
                output.Add(line.Data);
                waiting.RemoveWhere(marker => line.Data.Contains(marker, StringComparison.OrdinalIgnoreCase));
            }
        }

        process.OutputDataReceived += Received;
        process.ErrorDataReceived += Received;

        Log.Information("Starting {Project} and waiting for {Markers}", projectName, Quoted(markers));

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Stopwatch running = Stopwatch.StartNew();
        bool exitedOnItsOwn = false;

        while (running.Elapsed < ExampleStartTimeout)
        {
            lock (output)
            {
                if (waiting.Count == 0)
                {
                    break;
                }
            }

            if (process.WaitForExit(250))
            {
                exitedOnItsOwn = true;
                break;
            }
        }

        string[] missing;
        lock (output)
        {
            missing = [.. waiting];
        }

        if (exitedOnItsOwn)
        {
            // The overload with no timeout is the one that waits for the asynchronous readers as well,
            // so the tail below is the whole of what the application said before it stopped.
            process.WaitForExit();

            Assert.Fail($"{projectName} exited with code {process.ExitCode} after {running.Elapsed.TotalSeconds:F0}s "
                + $"instead of running, and never said {Quoted(missing)}.{Tail(output)}");
        }

        // A kill rather than Ctrl+C: on Windows a console control event goes to a process group rather
        // than to one process, so sending one would stop this build too. What a graceful shutdown does is
        // the hosted service's business and the unit suite's; what this target asks is whether the
        // application starts and works at all.
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.WaitForExit();

        if (missing.Length > 0)
        {
            Assert.Fail($"{projectName} was still running after {ExampleStartTimeout.TotalSeconds:F0}s "
                + $"but never said {Quoted(missing)}.{Tail(output)}");
        }

        Log.Information("{Project} said all of it, {Elapsed:F0}s in", projectName, running.Elapsed.TotalSeconds);
    }

    static string Quoted(IReadOnlyCollection<string> markers) =>
        string.Join(" and ", markers.Select(x => $"'{x}'"));

    /// <summary>
    /// The end of what an example printed, which is where the reason it stopped or stalled is.
    /// </summary>
    static string Tail(List<string> output)
    {
        lock (output)
        {
            return Environment.NewLine + "The last of its output:" + Environment.NewLine
                + string.Join(Environment.NewLine, output.TakeLast(60));
        }
    }

    /// <summary>
    /// A loopback port nothing is listening on, found by binding one and letting it go.
    /// </summary>
    static int FreeLoopbackPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            return ((IPEndPoint) listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    static readonly string[] DatabaseCategories =
        ["db-postgres", "db-sqlserver", "db-mysql", "db-oracle", "db-firebird", "db-sqlite", "db-redis"];

    /// <summary>
    /// The category a release gate carries, and the one thing every integration leg leaves out.
    /// </summary>
    /// <remarks>
    /// The clustered soak (<c>ClusteredSoakTestBase</c>) runs for half an hour by design, and a leg
    /// that ran it would read as a hung job rather than as thoroughness. It is run by hand before a
    /// tag, exactly as the benchmarks are; <c>BenchmarkSmoke</c> excludes the same name on the
    /// benchmark side, through <c>BenchmarkCategories</c>.
    /// </remarks>
    const string LongRunningCategory = "LongRunning";

    /// <summary>
    /// What one integration leg runs: the fixtures for its database, and never a release gate.
    /// </summary>
    /// <remarks>
    /// The <c>LongRunning</c> exclusion is applied whether or not a database was named, because an
    /// unnamed one applies no filter at all — which is what a local <c>dotnet fallout IntegrationTest</c>
    /// does, and it would otherwise pick up an hour of soak.
    /// </remarks>
    string GetTestFilter(string database)
    {
        string databaseFilter = database switch
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

        var excludeLongRunning = $"TestCategory!={LongRunningCategory}";
        return string.IsNullOrEmpty(databaseFilter) ? excludeLongRunning : $"{databaseFilter}&{excludeLongRunning}";
    }

    /// <summary>
    /// The integration suite, against the database <see cref="Database" /> names or against every one of
    /// them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate is about CI legs, and only about CI legs. Docker is what every fixture here needs, and
    /// of the three images the workflows run on only Ubuntu has a daemon — so the Windows and macOS legs
    /// skip the target rather than failing it, and it is the negation that says so.
    /// </para>
    /// <para>
    /// It used to say <c>Host is GitHubActions &amp;&amp; Linux</c>, which also skipped every developer
    /// machine: <c>build.cmd Compile UnitTest IntegrationTest</c> — the command CONTRIBUTING.md, the pull
    /// request template and AGENTS.md all give — reported the target as skipped and exited zero, so a
    /// contributor asked to run the integration tests before opening a pull request ran none of them and
    /// was told nothing. A local run has a daemon or it does not, and Testcontainers says which in a
    /// message worth reading; a silent skip says nothing at all.
    /// </para>
    /// </remarks>
    Target IntegrationTest => _ => _
        .DependsOn<ICompile>()
        .Before<IPack>()
        .OnlyWhenDynamic(() => Host is not GitHubActions || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
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
        .SetVersionSuffix(VersionSuffix)
        .SetContinuousIntegrationBuild(IsContinuousIntegrationBuild);

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
                .Where(x => x.GetProperty("IsPackable") != "false" || x.Name.Contains("Example"));

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
