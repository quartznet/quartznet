using BenchmarkDotNet.Attributes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz.Benchmark;

/// <summary>
/// What one firing of a delegate job's handler costs, bound by reflection (#3867's phase A) and bound by
/// the code the source generator writes (#3882).
/// </summary>
/// <remarks>
/// <para>
/// Both arms are one handler of one shape — a service, the firing and its token, returning a
/// <see cref="Task" /> — registered through the real <c>AddJob</c>, and read back from the registry the
/// scheduler would read it from. The first call is written with the lambda at the call, which this
/// project's build intercepts; the second passes the same lambda as a <see cref="Delegate" />, which it
/// cannot. What is measured is <see cref="DelegateJobBinding.Invoke" /> alone, the part that differs:
/// the registry lookup and the job factory in front of it are the same for both.
/// </para>
/// <para>
/// The service is a singleton, so neither arm measures the container building one; both ask the scope for
/// it, which is the same work either way.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class DelegateJobBenchmark
{
    private DelegateJobBinding reflected = null!;
    private DelegateJobBinding compiled = null!;
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;

    [GlobalSetup]
    public void Setup()
    {
        ServiceCollection services = new();
        services.AddSingleton<Hits>();

        Delegate asDelegate = static (Hits hits, IJobExecutionContext context, CancellationToken cancellationToken) => hits.Record(cancellationToken);

        services.AddQuartz(q =>
        {
            q.AddJob("compiled", static (Hits hits, IJobExecutionContext context, CancellationToken cancellationToken) => hits.Record(cancellationToken));
            q.AddJob("reflected", asDelegate);
        });

        Dictionary<string, DelegateJobBinding> bindings = DelegateJobRegistry.For(services)
            .Declared(Options.DefaultName)
            .ToDictionary(x => x.Name, x => x.Binding, StringComparer.Ordinal);

        compiled = bindings["compiled"];
        reflected = bindings["reflected"];

        if (!compiled.IsCompiled || reflected.IsCompiled)
        {
            throw new InvalidOperationException(
                "The compiled arm has to be intercepted and the reflected one not, or the two arms measure the same thing. "
                + "Is Quartz.Analyzers still an analyzer of this project, with Quartz.targets imported?");
        }

        provider = services.BuildServiceProvider();
        scope = provider.CreateScope();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        scope.Dispose();
        provider.Dispose();
    }

    /// <summary>
    /// The handler never reads the firing, so neither arm is handed one: building a real context would
    /// measure the context.
    /// </summary>
    [Benchmark(Baseline = true)]
    public bool Reflected() => reflected.Invoke(null!, scope.ServiceProvider, CancellationToken.None).IsCompletedSuccessfully;

    [Benchmark]
    public bool Compiled() => compiled.Invoke(null!, scope.ServiceProvider, CancellationToken.None).IsCompletedSuccessfully;

    public sealed class Hits
    {
        private int count;

        public Task Record(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            count++;
            return Task.CompletedTask;
        }
    }
}
