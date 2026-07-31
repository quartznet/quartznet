using BenchmarkDotNet.Attributes;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class SimpleSemaphoreBenchmark
{
    private readonly SimpleSemaphore semaphore;
    private readonly Guid requestorId;

    public SimpleSemaphoreBenchmark()
    {
        semaphore = new SimpleSemaphore();
        requestorId = Guid.NewGuid();
    }

    [Benchmark]
    public async Task ObtainAndRelease()
    {
        await semaphore.ObtainLock(requestorId, null, SchedulerLock.TriggerAccess, CancellationToken.None);
        await semaphore.ReleaseLock(requestorId, SchedulerLock.TriggerAccess, CancellationToken.None);
    }
}