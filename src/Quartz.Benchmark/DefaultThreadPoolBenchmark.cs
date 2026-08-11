using BenchmarkDotNet.Attributes;

using Quartz.Impl;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class DefaultThreadPoolBenchmark
{
    [Benchmark(OperationsPerInvoke = 500_000)]
    public async Task TryRun_CompletedTask_MaxConcurrencyIsMaxValue_SingleThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = int.MaxValue
        };
        await threadPool.Initialize();

        for (var i = 0; i < 500_000; i++)
        {
            await threadPool.TryRun(() => ValueTask.CompletedTask);
        }

        await threadPool.Shutdown(true);
    }

    [Benchmark(OperationsPerInvoke = 1_000_000)]
    public async Task TryRun_CompletedTask_MaxConcurrencyIsMaxValue_MultiThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = int.MaxValue
        };
        await threadPool.Initialize();

        await Execute(threadPool, 20, 50_000, tp => tp.TryRun(() => ValueTask.CompletedTask));

        await threadPool.Shutdown(true);
    }

    [Benchmark(OperationsPerInvoke = 500_000)]
    public async Task TryRun_CompletedTask_MaxConcurrencyIsSixteen_SingleThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = 16
        };

        await threadPool.Initialize();

        for (var i = 0; i < 500_000; i++)
        {
            await threadPool.TryRun(() => ValueTask.CompletedTask);
        }

        await threadPool.Shutdown(true);
    }

    [Benchmark(OperationsPerInvoke = 1_000_000)]
    public async Task TryRun_CompletedTask_MaxConcurrencyIsSixteen_MultiThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = 16
        };

        await threadPool.Initialize();

        await Execute(threadPool, 20, 50_000, tp => tp.TryRun(() => ValueTask.CompletedTask));

        await threadPool.Shutdown(true);
    }

    /// <summary>
    /// The primary goal of this benchmark is to measure memory allocations.
    /// </summary>
    /// <remarks>
    /// Note that this includes the allocations for initializing the ThreadPool itself.
    /// </remarks>
    [Benchmark]
    public async Task TryRun_OneShot()
    {
        var threadPool = new DefaultThreadPool();
        threadPool.MaxConcurrency = int.MaxValue;
        await threadPool.Initialize();
        await threadPool.TryRun(() => ValueTask.CompletedTask);
        await threadPool.Shutdown(true);
    }

    private static async Task Execute(
        DefaultThreadPool scheduler,
        int threadCount,
        int iterationsPerThread,
        Func<DefaultThreadPool, ValueTask<bool>> action)
    {
        ManualResetEvent start = new ManualResetEvent(false);

        var tasks = new Task[threadCount];

        for (var i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                start.WaitOne();

                for (var j = 0; j < iterationsPerThread; j++)
                {
                    await action(scheduler);
                }
            });
        }

        start.Set();

        await Task.WhenAll(tasks);
    }
}
