using System.Collections.Immutable;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Quartz.Benchmark;

/// <summary>
/// The runtime comparison of #4214 as BenchmarkDotNet jobs, each built and run in a process of its own:
/// Quartz's two assets on .NET 10 and .NET 11, with and without runtime async, and with the 11.0.0 thread-pool
/// spin rule turned off.
/// </summary>
/// <remarks>
/// <para>
/// <c>--runtime-comparison</c> before the usual BenchmarkDotNet arguments selects it, and
/// <c>--runtime-comparison-reversed</c> runs the same jobs in the opposite order, for the second round. The jobs
/// run case by case: every job for one row, then every job for the next, in the order of the list below.
/// </para>
/// <list type="table">
///   <item><term>A</term><description>The <c>net10.0</c> build on .NET 10, the baseline the Ratio column reads against.</description></item>
///   <item><term>B</term><description>The same build rolled forward to .NET 11 with <c>DOTNET_ROLL_FORWARD=LatestMajor</c>: an application that moved runtime and kept its <c>net10.0</c> target.</description></item>
///   <item><term>C</term><description>The <c>net11.0</c> build with <c>Quartz.csproj</c>'s <c>Features</c> emptied, so Quartz's <c>net11.0</c> asset has compiler state machines.</description></item>
///   <item><term>D</term><description>The <c>net11.0</c> build as it ships, runtime async.</description></item>
///   <item><term>D, spin rule off</term><description>D with <c>DOTNET_ThreadPool_SpuriousDispatchNoSpinThreshold=0</c>, the workaround README.md gives for dotnet/runtime#137494.</description></item>
/// </list>
/// <para>
/// BenchmarkDotNet 0.16 builds each set of jobs that builds alike with an <c>ArtifactsPath</c> of its own under
/// <c>.bdn/</c>, so C's state-machine <c>Quartz.dll</c> and D's runtime-async one are compiled into different
/// folders. That matters: MSBuild does not rebuild a project because a global property changed, so had the two
/// shared a folder, one could have copied the other's. Each fire-throughput benchmark prints
/// <see cref="RuntimeInfo.Describe" /> from its <c>[GlobalSetup]</c>, which BenchmarkDotNet writes to the log
/// under the job that ran it: that line, from the measured process itself, is what says which runtime and which
/// asset a row measured.
/// </para>
/// </remarks>
internal static class RuntimeComparison
{
    public const string Option = "--runtime-comparison";

    public const string ReversedOption = "--runtime-comparison-reversed";

    /// <summary>
    /// Takes <c>--job &lt;name&gt;</c> off the arguments and answers the predefined job it names, which the
    /// comparison's jobs are then built on, or <see cref="Job.Default" /> when there is none.
    /// </summary>
    /// <remarks>
    /// Left on the command line, a job would be added beside the five rather than shape them, and every row would
    /// run a sixth time on whatever runtime the host has. <c>--job dry</c> is how the comparison is checked before
    /// a timing window: every job built and run once, nothing measured.
    /// </remarks>
    public static Job TakeBaseJob(List<string> arguments)
    {
        int index = arguments.FindIndex(x => x is "--job" or "-j");
        if (index < 0 || index + 1 >= arguments.Count)
        {
            return Job.Default;
        }

        string name = arguments[index + 1];
        arguments.RemoveRange(index, 2);
        return name.ToLowerInvariant() switch
        {
            "dry" => Job.Dry,
            "short" => Job.ShortRun,
            "medium" => Job.MediumRun,
            "long" => Job.LongRun,
            "default" => Job.Default,
            _ => throw new ArgumentException($"--job {name} is not one of dry, short, medium, long or default, which are the jobs {Option} can be built on."),
        };
    }

    /// <summary>The configuration for the comparison, its jobs in the order they run.</summary>
    public static IConfig Config(bool reversed, Job baseJob)
    {
        Job a = baseJob.WithRuntime(CoreRuntime.Core10_0).WithId("A_net10").AsBaseline();
        Job b = baseJob.WithRuntime(CoreRuntime.Core10_0)
            .WithEnvironmentVariable("DOTNET_ROLL_FORWARD", "LatestMajor")
            .WithId("B_net10_on_11");
        Job c = baseJob.WithRuntime(CoreRuntime.Core11_0)
            .WithArguments([new MsBuildArgument("/p:Features=")])
            .WithId("C_net11_state_machines");
        Job d = baseJob.WithRuntime(CoreRuntime.Core11_0).WithId("D_net11");
        Job knob = baseJob.WithRuntime(CoreRuntime.Core11_0)
            .WithEnvironmentVariable("DOTNET_ThreadPool_SpuriousDispatchNoSpinThreshold", "0")
            .WithId("E_net11_spin_rule_off");

        Job[] jobs = [a, b, c, d, knob];
        if (reversed)
        {
            Array.Reverse(jobs);
        }

        return ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(jobs)
            .WithOrderer(new JobListOrderer(jobs));
    }

    /// <summary>
    /// Runs every job of one row before the next row, in the order the jobs were given, and otherwise orders
    /// as BenchmarkDotNet does.
    /// </summary>
    private sealed class JobListOrderer(Job[] jobs) : IOrderer
    {
        private readonly IOrderer inner = DefaultOrderer.Instance;

        public bool SeparateLogicalGroups => inner.SeparateLogicalGroups;

        public IEnumerable<BenchmarkCase> GetExecutionOrder(ImmutableArray<BenchmarkCase> benchmarksCase, IEnumerable<BenchmarkLogicalGroupRule>? order = null)
        {
            List<BenchmarkCase> byDefault = [.. inner.GetExecutionOrder(benchmarksCase, order)];
            List<string> rows = [.. byDefault.Select(RowKey).Distinct()];
            return byDefault
                .OrderBy(x => rows.IndexOf(RowKey(x)))
                .ThenBy(x => Array.FindIndex(jobs, job => job.Id == x.Job.Id));
        }

        public IEnumerable<BenchmarkCase> GetSummaryOrder(ImmutableArray<BenchmarkCase> benchmarksCases, Summary summary)
            => inner.GetSummaryOrder(benchmarksCases, summary);

        public string? GetHighlightGroupKey(BenchmarkCase benchmarkCase) => inner.GetHighlightGroupKey(benchmarkCase);

        public string? GetLogicalGroupKey(ImmutableArray<BenchmarkCase> allBenchmarksCases, BenchmarkCase benchmarkCase)
            => inner.GetLogicalGroupKey(allBenchmarksCases, benchmarkCase);

        public IEnumerable<IGrouping<string, BenchmarkCase>> GetLogicalGroupOrder(
            IEnumerable<IGrouping<string, BenchmarkCase>> logicalGroups,
            IEnumerable<BenchmarkLogicalGroupRule>? order = null)
            => inner.GetLogicalGroupOrder(logicalGroups, order);

        private static string RowKey(BenchmarkCase benchmarkCase)
            => benchmarkCase.Descriptor.DisplayInfo + "|" + benchmarkCase.Parameters.DisplayInfo;
    }
}
