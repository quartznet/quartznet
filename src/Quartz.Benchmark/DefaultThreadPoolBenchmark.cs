using BenchmarkDotNet.Attributes;

using Quartz.Simpl;

namespace Quartz.Benchmark;

[MemoryDiagnoser]
public class DefaultThreadPoolBenchmark
{
    [Benchmark(OperationsPerInvoke = 500_000)]
    public void RunInThread_CompletedTask_MaxConcurrencyIsMaxValue_SingleThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = int.MaxValue
        };
        threadPool.Initialize();

        for (var i = 0; i < 500_000; i++)
        {
            threadPool.RunInThread(() => Task.CompletedTask);
        }

        threadPool.Shutdown(true);
    }

    [Benchmark(OperationsPerInvoke = 1_000_000)]
    public async Task RunInThread_CompletedTask_MaxConcurrencyIsMaxValue_MultiThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = int.MaxValue
        };
        threadPool.Initialize();

        await Execute(threadPool, 20, 50_000, (tp) => tp.RunInThread(() => Task.CompletedTask));

        threadPool.Shutdown(true);
    }

    [Benchmark(OperationsPerInvoke = 500_000)]
    public void RunInThread_CompletedTask_MaxConcurrencyIsSixteen_SingleThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = 16
        };
        
        threadPool.Initialize();

        for (var i = 0; i < 500_000; i++)
        {
            threadPool.RunInThread(() => Task.CompletedTask);
        }

        threadPool.Shutdown(true);
    }

    [Benchmark(OperationsPerInvoke = 1_000_000)]
    public async Task RunInThread_CompletedTask_MaxConcurrencyIsSixteen_MultiThreaded()
    {
        var threadPool = new DefaultThreadPool
        {
            MaxConcurrency = 16
        };
        
        threadPool.Initialize();

        await Execute(threadPool, 20, 50_000, (tp) => tp.RunInThread(() => Task.CompletedTask));

        threadPool.Shutdown(true);
    }

    /// <summary>
    /// The primary goal of this benchmark is to measure memory allocations.
    /// </summary>
    /// <remarks>
    /// Note that this includes the allocations for initializing the ThreadPool itself.
    /// </remarks>
    [Benchmark]
    public void RunInThread_OneShot()
    {
        var threadPool = new DefaultThreadPool();
        threadPool.MaxConcurrency = int.MaxValue;
        threadPool.Initialize();
        threadPool.RunInThread(() => Task.CompletedTask);
        threadPool.Shutdown(true);
    }

    private static async Task Execute(DefaultThreadPool scheduler, int threadCount, int iterationsPerThread, Action<DefaultThreadPool> action)
    {
        ManualResetEvent start = new ManualResetEvent(false);

        var tasks = new Task[threadCount];

        for (var i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                start.WaitOne();

                for (var j = 0; j < iterationsPerThread; j++)
                {
                    action(scheduler);
                }
            });
        }

        start.Set();

        await Task.WhenAll(tasks);
    }
}