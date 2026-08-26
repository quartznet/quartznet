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

    private static int Main(string[] args)
    {
        bool smoke = args.Contains(SmokeOption, StringComparer.OrdinalIgnoreCase);
        if (smoke && args.Length > 1)
        {
            Console.Error.WriteLine($"{SmokeOption} takes no other arguments: it is a whole run, not a modifier on one.");
            return 1;
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
