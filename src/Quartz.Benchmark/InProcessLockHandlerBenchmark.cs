using BenchmarkDotNet.Attributes;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class InProcessLockHandlerBenchmark
{
    private readonly InProcessLockHandler lockHandler;
    private readonly Guid requestorId;

    public InProcessLockHandlerBenchmark()
    {
        lockHandler = new InProcessLockHandler();
        requestorId = Guid.NewGuid();
    }

    [Benchmark]
    public async Task ObtainAndRelease()
    {
        await lockHandler.AcquireLock(requestorId, null, SchedulerLock.TriggerAccess, CancellationToken.None);
        await lockHandler.ReleaseLock(requestorId, SchedulerLock.TriggerAccess, CancellationToken.None);
    }
}