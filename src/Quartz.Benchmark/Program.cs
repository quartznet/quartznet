using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace Quartz.Benchmark;

internal static class Program
{
    /// <summary>
    /// The smoke run: every benchmark in the assembly executed once, in this process, with nothing
    /// measured. It is what the <c>BenchmarkSmoke</c> build target runs on every pull request, and what
    /// to run by hand before changing a benchmark. See <see cref="SmokeConfig" /> for what it does and
    /// why it is a switch of ours rather than a line of BenchmarkDotNet options.
    /// </summary>
    private const string SmokeOption = "--smoke";

    /// <summary>
    /// The switcher options that print something and run nothing, so that an empty run is only a
    /// failure when the caller asked for benchmarks to be executed.
    /// </summary>
    private static readonly string[] printOnlyOptions = ["--help", "--version", "--list", "--info"];

    /// <summary>
    /// The runs that are not BenchmarkDotNet, each a whole run rather than a modifier on one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three of them loop one workload in this process with the harness out of the way, which is what a
    /// sampling profiler wants to attach to: no process per case, no pilot deciding how many invocations
    /// an iteration gets, and no measurement machinery in the stacks. They measure nothing themselves —
    /// <c>ultra</c> or an EventPipe session does that from outside, and
    /// <c>README.md</c> says how. The fourth measures something BenchmarkDotNet's model cannot express,
    /// because the interval it reports begins on one thread and ends on another.
    /// </para>
    /// <para>
    /// They are development tools and are outside every build target. Nothing in CI runs them, and the
    /// numbers they produce belong in an issue or a pull request rather than in this file.
    /// </para>
    /// </remarks>
    private static readonly (string Option, string Description, Action Run)[] developerRuns =
    [
        ("--profile-fire",
            "Fires against RAMJobStore at MaxConcurrency 10 for about 25 seconds. Attach a profiler to this for the fire path.",
            ProfileFire),
        ("--profile-cron",
            "Chains CronExpression.GetNextValidTimeAfter a hundred at a time for about 20 seconds. Attach a profiler to this for cron.",
            ProfileCron),
        ("--profile-schedule",
            "Schedules into a started scheduler for about 20 seconds, clearing the store every 50,000. Attach a profiler to this for ScheduleJob.",
            ProfileSchedule),
        ("--latency",
            "Schedules one job for now on an idle scheduler, 200 times, and prints the schedule-to-execute percentiles and where they go.",
            LatencyProbe.Run),
        ("--one-off-census",
            "Drains one-off firings against PostgreSQL and prints the commits, the statements and every statement by name. Needs QUARTZ_BENCHMARK_POSTGRES and pg_stat_statements.",
            OneOffCensus.Run),
    ];

    private static int Main(string[] args)
    {
        bool smoke = args.Contains(SmokeOption, StringComparer.OrdinalIgnoreCase);
        if (smoke && args.Length > 1)
        {
            Console.Error.WriteLine($"{SmokeOption} takes no other arguments: it is a whole run, not a modifier on one.");
            return 1;
        }

        if (args.Any(argument => "--help".Equals(argument, StringComparison.OrdinalIgnoreCase)))
        {
            PrintDeveloperRuns();
        }

        foreach ((string option, string _, Action run) in developerRuns)
        {
            if (!args.Contains(option, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (args.Length > 1)
            {
                Console.Error.WriteLine($"{option} takes no other arguments: it is a whole run, not a modifier on one.");
                return 1;
            }

            run();
            return 0;
        }

        // The filter is passed even in smoke mode, because a switcher given no selection at all asks the
        // console which benchmark to run, and CI has nobody to ask.
        string[] switcherArguments = smoke ? ["--filter", "*"] : args;

        List<Summary> summaries;
        try
        {
            summaries =
            [
                .. BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(switcherArguments, smoke ? SmokeConfig() : null)
            ];
        }
        catch (Exception exception)
        {
            // A benchmark that throws while running in this process takes the run down with it: the
            // exception comes back out through the switcher instead of being recorded as a failed case,
            // and the cases queued behind it never run. Reported here rather than left to become an
            // unhandled exception, whose exit code reads as a crash in the runtime. The benchmark that
            // threw is the last one the log names above this.
            Console.Error.WriteLine("The run stopped on an exception, so the benchmarks after it did not run:");
            Console.Error.WriteLine(exception);
            return 1;
        }

        return Report(summaries, args);
    }

    /// <summary>
    /// A dry run of everything that can run unattended, arranged to be worth having on every pull
    /// request rather than to measure anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is a configuration rather than a line of switcher options because none of what it decides can
    /// be spelled on BenchmarkDotNet's command line. The switcher selects categories —
    /// <c>--anyCategories</c>, <c>--allCategories</c> — but cannot reject one, and this run is defined by
    /// what it leaves out: a benchmark written tomorrow is in it without anybody remembering to add it,
    /// which a list of names on a command line could never promise.
    /// </para>
    /// <para>
    /// The rest is what makes a couple of hundred cases affordable on every pull request. Running in
    /// this process skips a build and a process launch per case, and
    /// <see cref="InProcessNoEmitToolchain" /> is the half of that pair <c>--inProcess</c> cannot ask
    /// for — it binds delegates where the other emits an assembly per case. Not enforcing a power plan
    /// is the large one on Windows, where BenchmarkDotNet otherwise switches the machine to High
    /// Performance and back around every case: 249 cases took 81 seconds with that and 20 without it,
    /// and a smoke run has no business touching the machine's power settings for measurements it throws
    /// away. An empty configuration exports nothing, so the run leaves no artefacts to upload, ignore or
    /// commit.
    /// </para>
    /// <para>
    /// What it keeps is the mandatory validators BenchmarkDotNet adds to every configuration, including
    /// the one that refuses a non-optimized assembly — a smoke run built in Debug would prove nothing,
    /// and is refused rather than tolerated.
    /// </para>
    /// </remarks>
    private static IConfig SmokeConfig()
    {
        return ManualConfig.CreateEmpty()
            .AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddJob(Job.Dry.WithToolchain(InProcessNoEmitToolchain.Instance).DontEnforcePowerPlan())
            .AddFilter(new SimpleFilter(benchmark => !benchmark.Descriptor.Categories.Any(
                category => BenchmarkCategories.ExcludedFromSmokeRun.Contains(category, StringComparer.OrdinalIgnoreCase))))
            .WithOptions(ConfigOptions.DisableLogFile);
    }

    /// <summary>
    /// Turns what BenchmarkDotNet reported into an exit code.
    /// </summary>
    /// <remarks>
    /// The switcher returns the same way whether the benchmarks ran or threw — a case that fails is a
    /// row of <c>NA</c> in the summary table and nothing else — so a caller that only watches the exit
    /// code cannot tell a healthy harness from one that failed on every case. That is what the smoke
    /// run is checking, so the reports are read here.
    /// </remarks>
    private static int Report(IReadOnlyList<Summary> summaries, string[] args)
    {
        List<string> failures = [];

        foreach (Summary summary in summaries)
        {
            failures.AddRange(summary.ValidationErrors.Where(error => error.IsCritical).Select(error => error.Message));
            failures.AddRange(summary.Reports.Where(report => !report.Success).Select(report => report.BenchmarkCase.DisplayInfo));
        }

        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"{failures.Count} benchmark(s) did not run to completion:");
            foreach (string failure in failures.Order(StringComparer.Ordinal))
            {
                Console.Error.WriteLine("  " + failure);
            }

            return 1;
        }

        if (summaries.Sum(summary => summary.Reports.Length) == 0
            && !args.Any(argument => printOnlyOptions.Contains(argument, StringComparer.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine(
                "No benchmark was executed. A filter that matches nothing exits exactly as a healthy run does, "
                + "so it fails here rather than passing as one.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Prints the runs that are ours rather than BenchmarkDotNet's, above the switcher's own help.
    /// </summary>
    private static void PrintDeveloperRuns()
    {
        Console.WriteLine("Runs of this assembly that are not BenchmarkDotNet. Each is a whole run and takes no other arguments:");
        Console.WriteLine();
        Console.WriteLine($"  {SmokeOption,-18}  Every benchmark executed once with nothing measured. What the BenchmarkSmoke target runs.");

        foreach ((string option, string description, Action _) in developerRuns)
        {
            Console.WriteLine($"  {option,-18}  {description}");
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Fires against <c>RAMJobStore</c> at the shipped pool size until the time is up, which is the
    /// workload <see cref="FireThroughputBenchmark" /> measures with the measuring taken out.
    /// </summary>
    private static void ProfileFire()
    {
        IScheduler scheduler = FireThroughput.StartScheduler(
            instanceName: "ProfileFire",
            maxConcurrency: 10,
            configureStore: quartz => quartz.UseInMemoryStore()).GetAwaiter().GetResult();

        try
        {
            LoopFires(TimeSpan.FromSeconds(25));
        }
        finally
        {
            FireThroughput.StopScheduler(scheduler).GetAwaiter().GetResult();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoopFires(TimeSpan duration)
    {
        long started = Stopwatch.GetTimestamp();
        long fires = 0;

        while (Stopwatch.GetElapsedTime(started) < duration)
        {
            FireThroughput.AwaitFires(FireThroughput.RamFiresPerInvocation);
            fires += FireThroughput.RamFiresPerInvocation;
        }

        Report("firings", fires, Stopwatch.GetElapsedTime(started));
    }

    /// <summary>
    /// Chains next-occurrence calls off each other until the time is up, which is
    /// <see cref="CronExpressionComparisonBenchmark.Next100" /> with the measuring taken out.
    /// </summary>
    private static void ProfileCron()
    {
        CronExpressionComparisonBenchmark benchmark = new();
        benchmark.GlobalSetup();

        LoopCron(benchmark, TimeSpan.FromSeconds(20));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoopCron(CronExpressionComparisonBenchmark benchmark, TimeSpan duration)
    {
        long started = Stopwatch.GetTimestamp();
        long calls = 0;

        while (Stopwatch.GetElapsedTime(started) < duration)
        {
            benchmark.Next100();
            calls += 100;
        }

        Report("next-occurrence calls", calls, Stopwatch.GetElapsedTime(started));
    }

    /// <summary>
    /// Schedules into a started scheduler until the time is up, clearing the store between invocations
    /// exactly as <see cref="ScheduleJobBenchmark" /> does, so that what is profiled is the call rather
    /// than a sorted set growing without bound.
    /// </summary>
    private static void ProfileSchedule()
    {
        ScheduleJobBenchmark benchmark = new();
        benchmark.GlobalSetup();

        try
        {
            LoopSchedules(benchmark, TimeSpan.FromSeconds(20));
        }
        finally
        {
            benchmark.GlobalCleanup();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoopSchedules(ScheduleJobBenchmark benchmark, TimeSpan duration)
    {
        long started = Stopwatch.GetTimestamp();
        long schedules = 0;

        while (Stopwatch.GetElapsedTime(started) < duration)
        {
            benchmark.IterationSetup();
            benchmark.ScheduleJob_SimpleTrigger();
            schedules += ScheduleJobBenchmark.SchedulesPerInvocation;
        }

        Report("schedules", schedules, Stopwatch.GetElapsedTime(started));
    }

    /// <summary>
    /// What the loop got through, so that a capture can be checked against the rate the benchmark
    /// reports rather than assumed to have measured the same thing.
    /// </summary>
    private static void Report(string unit, long operations, TimeSpan elapsed)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{operations:N0} {unit} in {elapsed.TotalSeconds:F1} s — {operations / elapsed.TotalSeconds:N0}/s, {elapsed.TotalNanoseconds / operations:N0} ns each."));
    }

    /// <summary>
    /// Runs one benchmark with BenchmarkDotNet out of the way, which is what a profiler wants to attach
    /// to. Nothing calls it — call it from <see cref="Main" /> when you need it.
    /// </summary>
    private static void DispatchBenchmark()
    {
        var benchmark = new JobDispatchBenchmark();
        benchmark.Run().GetAwaiter().GetResult();

        RunDispatch(benchmark);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunDispatch(JobDispatchBenchmark benchmark)
    {
        for (int i = 0; i < 100; ++i)
        {
            benchmark.Run().GetAwaiter().GetResult();
        }
    }
}
