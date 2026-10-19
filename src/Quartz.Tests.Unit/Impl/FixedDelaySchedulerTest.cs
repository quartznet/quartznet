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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// A simple trigger that measures its interval from completion, through a running scheduler on real
/// time (#4140): a 300 ms job on a one-second interval starts at about 0, 1.3 and 2.6 seconds, and never
/// beside itself, where at a fixed rate it starts at 0, 1 and 2.
/// </summary>
/// <remarks>
/// <c>FixedDelayStoreTest</c> holds the stores to the hold and the fire time on a clock it owns. These
/// ask only what the scheduler thread adds: that what the store measures is the gap a job sees.
/// </remarks>
[TestFixture(OverlapStoreKind.InMemory)]
[TestFixture(OverlapStoreKind.Sqlite)]
[NonParallelizable]
public sealed class FixedDelaySchedulerTest
{
    private const string Group = "fixed-delay";

    private static readonly TimeSpan interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan runTime = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);

    /// <summary>The grain of the clock the job reads beside the one the store reads.</summary>
    private static readonly TimeSpan clockGrain = TimeSpan.FromMilliseconds(20);

    private readonly OverlapStoreKind kind;

    private SqliteTestDatabase? database;

    public FixedDelaySchedulerTest(OverlapStoreKind kind)
    {
        this.kind = kind;
    }

    [SetUp]
    public void Reset()
    {
        TimedJob.Reset();
        database = kind == OverlapStoreKind.Sqlite ? new SqliteTestDatabase("fixed-delay-scheduler") : null;
    }

    [TearDown]
    public void DropDatabase()
    {
        database?.Dispose();
    }

    [Test]
    public async Task EachRunStartsAnIntervalAfterThePreviousOneCompletedAndNeverBesideIt()
    {
        List<(TimeSpan Start, TimeSpan End)> runs = await RunThreeTimes(measureIntervalFromCompletion: true);

        for (int i = 1; i < runs.Count; i++)
        {
            TimeSpan gap = runs[i].Start - runs[i - 1].End;
            gap.Should().BeGreaterThanOrEqualTo(interval - clockGrain,
                $"run {i + 1} is due an interval after run {i} completed, and not before");
            gap.Should().BeLessThan(interval + TimeSpan.FromSeconds(5), "and is not held back long past it, whatever else the machine is doing");
        }

        (runs[2].Start - runs[0].Start).Should().BeGreaterThanOrEqualTo(2 * (interval + runTime) - 2 * clockGrain,
            "three 300 ms runs a second apart from completion start at about 0, 1.3 and 2.6 seconds");
        TimedJob.MaxConcurrent.Should().Be(1, "the trigger is held while a firing of it runs");
    }

    [Test]
    public async Task AtAFixedRateEachRunStartsAnIntervalAfterThePreviousOneStarted()
    {
        List<(TimeSpan Start, TimeSpan End)> runs = await RunThreeTimes(measureIntervalFromCompletion: false);

        (runs[2].Start - runs[0].Start).Should().BeLessThan(2 * (interval + runTime) - 2 * clockGrain,
            "the control: at a fixed rate the runs start at about 0, 1 and 2 seconds, sooner than measuring from completion could");
    }

    private async Task<List<(TimeSpan Start, TimeSpan End)>> RunThreeTimes(bool measureIntervalFromCompletion)
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        try
        {
            IJobDetail job = JobBuilder.Create<TimedJob>().WithIdentity("timed", Group).Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("timed", Group)
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever().MeasureIntervalFromCompletion(measureIntervalFromCompletion))
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
            while (TimedJob.Runs.Count < 3 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            TimedJob.Runs.Count.Should().BeGreaterThanOrEqualTo(3, $"three runs within {waitLimit.TotalSeconds:F0} s");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        return [.. TimedJob.Runs.OrderBy(x => x.Start).Take(3)];
    }

    private ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "fixed-delay-" + Guid.NewGuid().ToString("N");
                options.InstanceId = "one";
            });

            if (database is not null)
            {
                q.UsePersistentStore(persistent =>
                {
                    persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                    persistent.ProvisionSchema();
                });
            }
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Runs for <see cref="runTime" />, recording when each run started and ended on one monotonic clock.
    /// </summary>
    public sealed class TimedJob : IJob
    {
        private static readonly long origin = TimeProvider.System.GetTimestamp();
        private static int running;
        private static int maxConcurrent;

        public static ConcurrentBag<(TimeSpan Start, TimeSpan End)> Runs { get; private set; } = [];

        public static int MaxConcurrent => Volatile.Read(ref maxConcurrent);

        public static void Reset()
        {
            Runs = [];
            running = 0;
            maxConcurrent = 0;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            TimeSpan start = TimeProvider.System.GetElapsedTime(origin);
            int now = Interlocked.Increment(ref running);
            int seen;
            while (now > (seen = Volatile.Read(ref maxConcurrent)) && Interlocked.CompareExchange(ref maxConcurrent, now, seen) != seen)
            {
            }

            try
            {
                await Task.Delay(runTime, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref running);
                Runs.Add((start, TimeProvider.System.GetElapsedTime(origin)));
            }
        }
    }
}
