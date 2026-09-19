using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace Quartz.Benchmark.Competitors;

internal static class Program
{
    /// <summary>
    /// The category the scenarios that need a database carry, so the smoke run can leave them out.
    /// </summary>
    public const string RequiresDatabase = "RequiresDatabase";

    /// <summary>
    /// The smoke run: every scenario that can run unattended, executed once, with nothing measured.
    /// </summary>
    /// <remarks>
    /// Nothing in CI runs it — this project is outside <c>Quartz.slnx</c> on purpose — but it is what to
    /// run by hand after changing an engine, because an engine that has stopped executing jobs looks
    /// exactly like a slow one until something waits for an execution that never comes.
    /// </remarks>
    private const string SmokeOption = "--smoke";

    /// <summary>
    /// The switcher options that print something and run nothing, so that an empty run is only a
    /// failure when the caller asked for benchmarks to be executed.
    /// </summary>
    private static readonly string[] printOnlyOptions = ["--help", "--version", "--list", "--info"];

    /// <summary>
    /// The runs that are not BenchmarkDotNet, each a whole run rather than a modifier on one.
    /// </summary>
    private static readonly (string Option, string Description, Action Run)[] plainRuns =
    [
        ("--recurring",
            "S4: a hundred one-second schedules per engine for a minute, and how close to the second each firing landed.",
            S4RecurringAccuracy.Run),
        ("--commits",
            "S2's database census: commits and statements per execution, counted at PostgreSQL. Needs QUARTZ_BENCHMARK_POSTGRES.",
            S2CommitCensus.Run),
    ];

    private static int Main(string[] args)
    {
        bool smoke = args.Contains(SmokeOption, StringComparer.OrdinalIgnoreCase);
        if (smoke && args.Length > 1)
        {
            Console.Error.WriteLine($"{SmokeOption} takes no other arguments: it is a whole run, not a modifier on one.");
            return 1;
        }

        ScenarioConfig.SmokeRun = smoke;

        if (args.Any(argument => "--help".Equals(argument, StringComparison.OrdinalIgnoreCase)))
        {
            PrintPlainRuns();
        }

        foreach ((string option, string _, Action run) in plainRuns)
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
        // console which benchmark to run.
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
            Console.Error.WriteLine("The run stopped on an exception, so the benchmarks after it did not run:");
            Console.Error.WriteLine(exception);
            return 1;
        }

        return Report(summaries, args);
    }

    /// <summary>
    /// A dry run of everything that can run unattended.
    /// </summary>
    /// <remarks>
    /// Running in this process skips a build and a process launch per case, and not enforcing a power
    /// plan keeps a smoke run from switching the machine to High Performance and back around every
    /// case. <see cref="ScenarioConfig" /> is what keeps each case from also running under its own
    /// measuring job.
    /// </remarks>
    private static IConfig SmokeConfig()
    {
        ManualConfig config = ManualConfig.CreateEmpty()
            .AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddJob(Job.Dry.WithToolchain(InProcessNoEmitToolchain.Instance).DontEnforcePowerPlan())
            .AddFilter(new SimpleFilter(benchmark => !benchmark.Descriptor.Categories.Contains(RequiresDatabase, StringComparer.OrdinalIgnoreCase)))
            .WithOptions(ConfigOptions.DisableLogFile);

        return config;
    }

    /// <summary>
    /// Turns what BenchmarkDotNet reported into an exit code.
    /// </summary>
    /// <remarks>
    /// The switcher returns the same way whether the benchmarks ran or threw — a case that fails is a
    /// row of <c>NA</c> in the summary table and nothing else — so a caller that only watches the exit
    /// code cannot tell a healthy harness from one that failed on every case.
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

    private static void PrintPlainRuns()
    {
        Console.WriteLine("Runs of this assembly that are not BenchmarkDotNet. Each is a whole run and takes no other arguments:");
        Console.WriteLine();
        Console.WriteLine($"  {SmokeOption,-14}  Every scenario that does not need a database, executed once with nothing measured.");

        foreach ((string option, string description, Action _) in plainRuns)
        {
            Console.WriteLine($"  {option,-14}  {description}");
        }

        Console.WriteLine();
    }
}
