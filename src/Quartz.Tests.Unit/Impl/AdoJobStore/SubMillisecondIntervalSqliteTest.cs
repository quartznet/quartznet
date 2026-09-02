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

using Quartz.Impl;
using Quartz.Tests;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A repeat interval shorter than a millisecond is refused by a persistent store, loudly, instead of
/// being written as zero.
/// </summary>
/// <remarks>
/// <para>
/// <c>REPEAT_INTERVAL</c> holds whole milliseconds — <c>StdAdoDelegate.GetDbTimeSpanValue</c> casts
/// <c>TotalMilliseconds</c> to <c>long</c> — so anything shorter used to be stored as <c>0</c>. That is
/// not merely lossy: the trigger read back has a zero repeat interval, and
/// <c>SimpleTriggerImpl.GetFireTimeAfter</c> divides by it, so the trigger throws
/// <see cref="DivideByZeroException" /> on its next firing. <c>AdoJobStoreBase</c> catches that, logs
/// it and returns, leaving the row in <c>ACQUIRED</c> for good — a job that stops running and says
/// nothing. Found by the fire-throughput benchmark, filed as
/// <see href="https://github.com/quartznet/quartznet/issues/3673">#3673</see>.
/// </para>
/// <para>
/// The refusal is what makes the divide-by-zero unreachable, so the store's catch is left alone: it is
/// there for the failures nobody has thought of, and this one is now thought of.
/// </para>
/// <para>
/// SQLite in a file rather than a container, so the claim is pinned where the coverage gate can see
/// it. The rule is <c>StdAdoDelegate</c>'s and therefore every dialect's; nothing here is SQLite's.
/// </para>
/// </remarks>
public sealed class SubMillisecondIntervalSqliteTest
{
    /// <summary>Half a millisecond: representable in a <see cref="TimeSpan" />, not in the column.</summary>
    private static readonly TimeSpan SubMillisecond = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond / 2);

    private string databaseFile = null!;
    private string connectionString = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-submilli-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public void DeleteDatabase()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    [Test]
    public async Task SchedulingASubMillisecondRepeatIntervalIsRefusedRatherThanRoundedToZero()
    {
        await using SchedulerHandle handle = await BuildScheduler();

        Func<Task> act = () => handle.Scheduler.ScheduleJob(Job(), Trigger(SubMillisecond)).AsTask();

        (await act.Should().ThrowAsync<Exception>(
                "a store that cannot hold the interval has to say so at schedule time; writing zero produces a "
                + "trigger that throws DivideByZeroException on its next firing and then sits in ACQUIRED for ever")
            .WithMessage("*millisecond*"))
            .WithMessage("*REPEAT_INTERVAL*",
                "the message has to name the column, because that is what the caller has to change the value to fit");
    }

    /// <summary>
    /// The refusal is the store's, not the trigger's: a whole millisecond is fine, which is what makes
    /// the message's advice followable.
    /// </summary>
    [Test]
    public async Task AWholeMillisecondIsAccepted()
    {
        await using SchedulerHandle handle = await BuildScheduler();

        await handle.Scheduler.ScheduleJob(Job(), Trigger(TimeSpan.FromMilliseconds(1)));

        ITrigger? stored = await handle.Scheduler.GetTrigger(new TriggerKey("fast", "submilli"));

        stored.Should().BeOfType<Quartz.Impl.Triggers.SimpleTriggerImpl>()
            .Which.RepeatInterval.Should().Be(TimeSpan.FromMilliseconds(1),
                "a millisecond is exactly what the column holds, so it has to round-trip unchanged");
    }

    /// <summary>
    /// <c>RAMJobStore</c> keeps whatever it was given, and goes on doing so. The refusal is a property
    /// of what a database column can hold, and the in-memory store has no column.
    /// </summary>
    [Test]
    public async Task TheInMemoryStoreStillAcceptsIt()
    {
        RAMJobStore store = TestJobStores.Ram();
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "submilli-ram");
                q.UseJobStore(store);
            })
            .BuildScheduler();

        try
        {
            await scheduler.ScheduleJob(Job(), Trigger(SubMillisecond));

            ITrigger? stored = await scheduler.GetTrigger(new TriggerKey("fast", "submilli"));

            stored.Should().BeOfType<Quartz.Impl.Triggers.SimpleTriggerImpl>()
                .Which.RepeatInterval.Should().Be(SubMillisecond,
                    "the in-memory store holds the TimeSpan it was handed; there is no column to round it to");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static IJobDetail Job()
    {
        return JobBuilder.Create<NoOpIntervalJob>().WithIdentity("fast", "submilli").Build();
    }

    private static ITrigger Trigger(TimeSpan interval)
    {
        return TriggerBuilder.Create()
            .WithIdentity("fast", "submilli")
            // Far enough out that nothing fires while the test drives the store by hand.
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .WithSimpleSchedule(schedule => schedule.WithInterval(interval).RepeatForever())
            .Build();
    }

    private async Task<SchedulerHandle> BuildScheduler()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "submilli";
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString);
                store.ProvisionSchema();
            });
        });

        ServiceProvider container = services.BuildServiceProvider();

        // Never started: the trigger is a year out and the test drives the store directly.
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        return new SchedulerHandle(container, scheduler);
    }

    private sealed class SchedulerHandle : IAsyncDisposable
    {
        private readonly ServiceProvider container;

        public SchedulerHandle(ServiceProvider container, IScheduler scheduler)
        {
            this.container = container;
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
        }
    }

    public sealed class NoOpIntervalJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
