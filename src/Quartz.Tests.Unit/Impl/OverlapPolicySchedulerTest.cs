#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

#nullable enable

using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// An overlap policy through a running scheduler: what the scheduler thread does with the store's
/// answer, and what reaches the listeners and the history.
/// </summary>
/// <remarks>
/// <c>OverlapPolicyStoreTest</c> holds the stores to the decisions on a clock it owns. These run on real
/// time, so they only ask what cannot be seen from the store: that a replaced firing is interrupted, and
/// that a skipped one is recorded beside the misfires.
/// </remarks>
[NonParallelizable]
public sealed class OverlapPolicySchedulerTest
{
    private const string Group = "overlap";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);

    [SetUp]
    public void Reset()
    {
        HeldJob.Reset();
    }

    [TearDown]
    public void LetGo()
    {
        HeldJob.Stop();
    }

    [Test]
    public async Task CancelPreviousInterruptsTheRunningFiringWhenTheNextStarts()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await Schedule(scheduler, OverlapPolicy.CancelPrevious, TimeSpan.FromMilliseconds(300));

        await WaitFor(() => HeldJob.Cancelled.Count >= 1 && HeldJob.Started.Count >= 2,
            "the second firing to start and the first to be interrupted");

        HeldJob.Cancelled.First().Should().Be(HeldJob.Started.First(),
            "the firing interrupted is the one that was running when the next came due");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task SkipDropsWhatComesDueWhileAFiringRunsAndTheHistoryRecordsItAsAnOverlap()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await Schedule(scheduler, OverlapPolicy.Skip, TimeSpan.FromMilliseconds(200));

        IExecutionHistoryStore history = container.GetRequiredService<IExecutionHistoryStore>();
        List<MisfireHistoryEntry> rows = [];
        await WaitFor(async () =>
        {
            rows = (await history.QueryMisfires(new MisfireHistoryQuery
            {
                SchedulerName = scheduler.SchedulerName,
                Take = PagedQuery.All
            })).Items.ToList();

            return rows.Count >= 2;
        }, "two firings to be skipped while the first one is held");

        HeldJob.Started.Should().ContainSingle("nothing starts beside the running firing under Skip");
        rows.Should().OnlyContain(row => row.Reason == MisfireReason.Overlap,
            "every row is a firing the policy dropped, and none of them is a misfire");
        (await history.CountMisfires(scheduler.SchedulerName, DateTimeOffset.UtcNow.AddMinutes(-1))).Should().Be(0,
            "the summary counts misfires, and a skip is not one");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task BufferOneStartsTheHeldFiringOnceTheRunningOneEnds()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await Start(container);

        await Schedule(scheduler, OverlapPolicy.BufferOne, TimeSpan.FromMilliseconds(200));

        await WaitFor(() => HeldJob.Started.Count >= 1, "the first firing to start");
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        HeldJob.Started.Should().ContainSingle("the trigger is held while its firing runs");

        HeldJob.ReleaseOne();
        await WaitFor(() => HeldJob.Started.Count >= 2, "the held firing to start once the first ended");

        HeldJob.MaxConcurrent.Should().Be(1, "BufferOne never lets two firings of the trigger run at once");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    private static async Task Schedule(IScheduler scheduler, OverlapPolicy policy, TimeSpan interval)
    {
        IJobDetail job = JobBuilder.Create<HeldJob>().WithIdentity("held", Group).Build();
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("often", Group)
            .ForJob(job)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .WithOverlapPolicy(policy)
            .Build();

        await scheduler.ScheduleJob(job, trigger);
    }

    private static ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q => q.ConfigureScheduler(options =>
        {
            options.InstanceName = "overlap-" + Guid.NewGuid().ToString("N");
            options.InstanceId = "one";
        }));

        return services.BuildServiceProvider();
    }

    private static async Task<IScheduler> Start(ServiceProvider container)
    {
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return scheduler;
    }

    private static Task WaitFor(Func<bool> condition, string what) => WaitFor(() => Task.FromResult(condition()), what);

    private static async Task WaitFor(Func<Task<bool>> condition, string what)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out after {waitLimit.TotalSeconds:F0} s waiting for {what}.");
    }

    /// <summary>
    /// Runs until it is interrupted, let go one firing at a time, or the test ends.
    /// </summary>
    public sealed class HeldJob : IJob
    {
        private static CancellationTokenSource stop = new();
        private static SemaphoreSlim letGo = new(0);
        private static int running;
        private static int maxConcurrent;

        public static ConcurrentQueue<string> Started { get; private set; } = new();

        public static ConcurrentQueue<string> Cancelled { get; private set; } = new();

        public static int MaxConcurrent => Volatile.Read(ref maxConcurrent);

        public static void Reset()
        {
            stop = new CancellationTokenSource();
            letGo = new SemaphoreSlim(0);
            running = 0;
            maxConcurrent = 0;
            Started = new ConcurrentQueue<string>();
            Cancelled = new ConcurrentQueue<string>();
        }

        public static void ReleaseOne() => letGo.Release();

        public static void Stop() => stop.Cancel();

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            int now = Interlocked.Increment(ref running);
            int seen;
            while (now > (seen = Volatile.Read(ref maxConcurrent)) && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen)
            {
            }

            Started.Enqueue(context.FireInstanceId);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stop.Token);

            try
            {
                await letGo.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled.Enqueue(context.FireInstanceId);
            }
            catch (OperationCanceledException)
            {
                // The test is over.
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }
    }
}
