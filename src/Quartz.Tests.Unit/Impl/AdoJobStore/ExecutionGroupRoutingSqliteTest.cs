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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

using CountingSqliteDelegate = Quartz.Tests.Unit.Impl.AdoJobStore.AcquisitionBehindExecutingJobSqliteTest.CountingSqliteDelegate;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Execution-group routing on the shipped SQLite delegate, driven by hand (#4069): the acquisition
/// statement keeps unserved rows out, the misfire sweep leaves them alone, and the handler says once that
/// nobody is serving them.
/// </summary>
/// <remarks>
/// One store on a SQLite file with <c>ServeExecutionGroups("served")</c>. The delegate records how long
/// each acquisition read was asked to be, which is how the test tells a row kept out in SQL from a row
/// read and read past: the first costs nothing, the second a wider read.
/// </remarks>
[NonParallelizable]
public sealed class ExecutionGroupRoutingSqliteTest
{
    private const string Served = "served";
    private const string Unserved = "gpu";
    private static readonly TimeSpan misfireThreshold = TimeSpan.FromSeconds(1);

    private SqliteTestDatabase database = null!;
    private ServiceProvider node = null!;
    private IScheduler scheduler = null!;
    private AdoJobStoreBase store = null!;
    private FakeLoggerProvider logs = null!;

    [SetUp]
    public async Task CreateNode()
    {
        database = new SqliteTestDatabase("execution-group-routing");
        CountingSqliteDelegate.Reset();
        logs = new FakeLoggerProvider();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "execution-group-routing";
                options.InstanceId = "node";
            });
            q.ServeExecutionGroups(Served);

            q.UsePersistentStore(persistent =>
            {
                persistent.UseDriverDelegate<CountingSqliteDelegate>();
                persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                persistent.ProvisionSchema();
            });
        });

        node = services.BuildServiceProvider();
        scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        store = (AdoJobStoreBase) node.GetRequiredService<IJobStore>();

        // Set on the store itself, so a trigger ten seconds late is misfired by the sweep the tests
        // drive by hand; the scheduler is never started, so no handler of its own runs.
        store.MisfireThreshold = misfireThreshold;
    }

    [TearDown]
    public async Task DisposeNode()
    {
        await node.DisposeAsync();
        logs.Dispose();
        database.Dispose();
    }

    /// <summary>
    /// The row this node does not serve is never read: the statement keeps it out, so the read of one
    /// comes back with the served row and the round ends there — no read-past, no skipped row.
    /// </summary>
    [Test]
    public async Task AnEarlierDueUnservedTriggerIsKeptOutOfTheReadRatherThanReadPast()
    {
        DateTimeOffset due = TimeProvider.System.GetUtcNow().AddSeconds(30);
        await Schedule("gpu-1", due, Unserved);
        await Schedule("served-1", due.AddMilliseconds(1), Served);
        CountingSqliteDelegate.Reset();

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestFor(maxCount: 1));

        acquired.Should().ContainSingle().Which.Key.Name.Should().Be("served-1",
            "the gpu trigger sits first in the fire-time order and is not this node's to take");
        CountingSqliteDelegate.AcquisitionReadCounts.Should().Equal([1],
            "the clause keeps the unserved row out of the result, so the one read of the count asked for is the whole round");
        (await StateOf("gpu-1")).Should().Be("WAITING");
        (await StateOf("served-1")).Should().Be("ACQUIRED");
    }

    /// <summary>
    /// The misfire half: a sweep on this node re-arms a served trigger that missed its time and leaves an
    /// unserved one exactly as it is, however many thresholds late.
    /// </summary>
    [Test]
    public async Task TheMisfireSweepLeavesAnUnservedRowUntouched()
    {
        DateTimeOffset due = TimeProvider.System.GetUtcNow().AddSeconds(-10);
        await Schedule("gpu-1", due, Unserved);
        await Schedule("served-1", due, Served);
        long gpuBefore = await NextFireTimeOf("gpu-1");
        long servedBefore = await NextFireTimeOf("served-1");

        await store.RecoverMisfires(Guid.NewGuid());

        (await NextFireTimeOf("served-1")).Should().BeGreaterThan(servedBefore,
            "the control: a served trigger ten seconds late on a one-second threshold is misfired and its FireNow policy re-arms it");
        (await NextFireTimeOf("gpu-1")).Should().Be(gpuBefore,
            "a node's sweep handles only the groups it serves, so the gpu row waits, unmodified, for a node that does");
        (await StateOf("gpu-1")).Should().Be("WAITING");
    }

    /// <summary>
    /// The handler says so once: 3609 on the pass that first finds unserved misfires, not again on the
    /// next pass, and 3610 on the pass that finds them gone.
    /// </summary>
    [Test]
    public async Task UnservedMisfiresAreLoggedOnTheTransitionAndNotEveryPass()
    {
        DateTimeOffset due = TimeProvider.System.GetUtcNow().AddSeconds(-10);
        await Schedule("gpu-1", due, Unserved);
        await Schedule("gpu-2", due.AddSeconds(1), Unserved);

        await store.RecoverMisfires(Guid.NewGuid());
        await store.RecoverMisfires(Guid.NewGuid());

        List<FakeLogRecord> unserved = logs.Collector.GetSnapshot().Where(record => record.Id.Id == 3609).ToList();
        unserved.Should().ContainSingle("two passes found the same two unserved misfires; the second pass is not news")
            .Which.Message.Should().Contain("2 misfired trigger(s) belong to execution groups this node does not serve");
        unserved[0].Level.Should().Be(LogLevel.Warning);
        string? oldest = unserved[0].StructuredState.Should().Contain(pair => pair.Key == "OldestDueUtc", "the oldest fire time says how long nobody has served the group")
            .Which.Value;
        DateTimeOffset.Parse(oldest!, System.Globalization.CultureInfo.CurrentCulture).Should().BeCloseTo(due, TimeSpan.FromSeconds(1),
            "the earlier of the two unserved triggers is the oldest; the state is formatted to the second");

        // Another node served them: the rows are gone, and the next pass says so once.
        await scheduler.UnscheduleJob(new TriggerKey("gpu-1", "routing"));
        await scheduler.UnscheduleJob(new TriggerKey("gpu-2", "routing"));
        await store.RecoverMisfires(Guid.NewGuid());
        await store.RecoverMisfires(Guid.NewGuid());

        logs.Collector.GetSnapshot().Where(record => record.Id.Id == 3610).Should().ContainSingle(
            "the transition back is logged once, as the transition there was");
    }

    private static TriggerAcquisitionRequest RequestFor(int maxCount)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(5),
            MaxCount = maxCount,
            TimeWindow = TimeSpan.FromSeconds(5),
            ServedExecutionGroups = [ExecutionGroupScope.Named(Served)],
        };
    }

    /// <summary>
    /// Stores a trigger due at <paramref name="at" /> exactly, through the store rather than the
    /// scheduler: the scheduler recomputes a start time in the past to now, and a trigger that is to be
    /// ten seconds late has to be stored late.
    /// </summary>
    private async Task Schedule(string name, DateTimeOffset at, string executionGroup)
    {
        IJobDetail job = JobBuilder.Create<RoutedJob>().WithIdentity(name, "routing").Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, "routing")
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartAt(at)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever().WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();
        trigger.NextFireTimeUtc = at;

        await store.ScheduleJob(job, trigger);
    }

    private async Task<string?> StateOf(string triggerName)
    {
        return (string?) await Scalar("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName);
    }

    private async Task<long> NextFireTimeOf(string triggerName)
    {
        return (long) (await Scalar("SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name", triggerName))!;
    }

    private async Task<object?> Scalar(string sql, string triggerName)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", triggerName);
        return await command.ExecuteScalarAsync();
    }

    public sealed class RoutedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
