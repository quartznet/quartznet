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
        await semaphore.ObtainLock(requestorId, null, JobStoreSupport.LockTriggerAccess, CancellationToken.None);
        await semaphore.ReleaseLock(requestorId, JobStoreSupport.LockTriggerAccess, CancellationToken.None);
    }
}