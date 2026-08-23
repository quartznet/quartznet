using System.Collections.Frozen;

using BenchmarkDotNet.Attributes;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Benchmark;

/// <summary>
/// The two static lookup tables <see cref="SchedulerScopedServiceProvider" /> consults on the way to
/// every service a named scheduler's components resolve — including the job type and each of a job's
/// constructor dependencies, once per fire.
/// </summary>
/// <remarks>
/// <para>
/// Only a <em>named</em> scheduler pays this: <c>SchedulerScopedServiceProvider.For</c> hands the
/// container straight back when there is no service key, so the default scheduler never reaches these
/// tables at all. The benchmark therefore measures the named-scheduler case, which is the only one the
/// choice of collection can affect.
/// </para>
/// <para>
/// The two arms differ in nothing but the collection type, and both are built from the real member
/// lists rather than a copy of them, so what the ratio reports is exactly what swapping the collections
/// would buy. <see cref="ResolveThroughProvider" /> is the denominator: it is one whole
/// <see cref="IServiceProvider.GetService" /> through the real wrapper, so the ratio between it and the
/// lookup arms says what share of a resolution the lookups are.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class SchedulerScopedServiceProviderBenchmark
{
    /// <summary>
    /// Requests one job instantiation makes, in the proportion it makes them: the job type and its
    /// constructor dependencies miss both tables, and the scheduler's own parts hit the first.
    /// </summary>
    private static readonly Type[] requests =
    [
        typeof(SchedulerScopedServiceProviderBenchmark), // the job type: misses both tables
        typeof(IServiceScopeFactory),                    // a dependency the container answers: misses both
        typeof(TimeProvider),                            // the one type answered outside both tables
        typeof(ISchedulerFactory),                       // a scheduler-scoped part: hits the set
        typeof(IJobStore),                               // and another
    ];

    private HashSet<Type> scopedHashSet = null!;
    private FrozenSet<Type> scopedFrozenSet = null!;
    private Dictionary<Type, Func<IServiceProvider, string, object>> optionsDictionary = null!;
    private FrozenDictionary<Type, Func<IServiceProvider, string, object>> optionsFrozenDictionary = null!;

    private IServiceProvider scopedProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        scopedHashSet = [.. SchedulerScopedServiceProvider.SchedulerScopedServiceTypes];
        scopedFrozenSet = scopedHashSet.ToFrozenSet();

        optionsDictionary = SchedulerScopedServiceProvider.DeclareQuartzOptions();
        optionsFrozenDictionary = optionsDictionary.ToFrozenDictionary();

        ServiceCollection services = new();
        services.AddQuartz("reporting", static _ => { });
        scopedProvider = SchedulerScopedServiceProvider.For(services.BuildServiceProvider(), "reporting");
    }

    [Benchmark(Baseline = true)]
    public int Dictionaries()
    {
        int answered = 0;
        foreach (Type request in requests)
        {
            if (scopedHashSet.Contains(request) || optionsDictionary.ContainsKey(request))
            {
                answered++;
            }
        }

        return answered;
    }

    [Benchmark]
    public int Frozen()
    {
        int answered = 0;
        foreach (Type request in requests)
        {
            if (scopedFrozenSet.Contains(request) || optionsFrozenDictionary.ContainsKey(request))
            {
                answered++;
            }
        }

        return answered;
    }

    /// <summary>
    /// One resolution through the real wrapper, for scale: everything the lookups above are a part of.
    /// </summary>
    [Benchmark]
    public object? ResolveThroughProvider()
    {
        return scopedProvider.GetService(typeof(SchedulerScopedServiceProviderBenchmark));
    }
}
