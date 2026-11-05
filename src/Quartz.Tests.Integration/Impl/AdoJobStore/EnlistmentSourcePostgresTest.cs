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

using System.Data.Common;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Quartz.Extensibility;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// <see cref="AdoJobStoreOptions.EnlistmentSource" /> on PostgreSQL, where the scheduling signal is
/// deferred to the application's commit - the one thing a unit test over SQLite cannot reach, because
/// SQLite locks in process and the store defers no signal there.
/// </summary>
/// <remarks>
/// The scheduler disposes the scope a source returns as the call ends, and disposing an enlistment of a
/// <see cref="DbTransaction" /> is what raises the signal that call deferred. A source that returns the
/// enlistment as it is signals while the application's transaction is open; one that returns a scope of its
/// own, and ends the enlistment after the commit, signals when the rows are visible.
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class EnlistmentSourcePostgresTest
{
    private const string SchedulerName = "EnlistmentSourcePostgres";

    private static readonly JobKey jobKey = new("source-driven", SchedulerName);

    private ServiceProvider? container;
    private NpgsqlConnection? applicationConnection;
    private DbTransaction? applicationTransaction;
    private readonly List<IDisposable> deferredScopes = [];

    [TearDown]
    public async Task TearDown()
    {
        EndDeferredScopes();

        if (applicationTransaction is not null)
        {
            await applicationTransaction.DisposeAsync();
            applicationTransaction = null;
        }

        if (applicationConnection is not null)
        {
            await applicationConnection.DisposeAsync();
            applicationConnection = null;
        }

        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }
    }

    [Test]
    public async Task SchedulingThenRollingBackDiscardsTheScheduleAndCommittingKeepsIt()
    {
        IScheduler scheduler = await GetScheduler(EnlistApplicationTransaction);
        await scheduler.Clear();

        await BeginUnitOfWork();
        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());
        (await scheduler.Exists(jobKey)).Should().BeTrue("the store wrote through the application's transaction");
        await EndUnitOfWork(commit: false);

        (await scheduler.Exists(jobKey)).Should().BeFalse("the application rolled back");

        await BeginUnitOfWork();
        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());
        await EndUnitOfWork(commit: true);

        (await scheduler.Exists(jobKey)).Should().BeTrue("the application committed");

        await scheduler.Clear();
    }

    /// <summary>
    /// The case the rest of this class exists for: a running scheduler, idle for two minutes unless
    /// something wakes it, and a trigger that is due the moment the application commits.
    /// </summary>
    [Test]
    public async Task ASourceThatEndsItsScopeAfterTheCommitHasTheJobFireRightAfterIt()
    {
        IScheduler scheduler = await GetScheduler(DeferScopeUntilTheTestEnds, idleWaitTime: TimeSpan.FromMinutes(2));

        try
        {
            await scheduler.Clear();
            SignallingJob.Reset();
            await scheduler.Start();

            await BeginUnitOfWork();
            await scheduler.ScheduleJob(
                JobBuilder.Create<SignallingJob>().WithIdentity(jobKey).StoreDurably().Build(),
                TriggerBuilder.Create().WithIdentity(jobKey.Name, jobKey.Group).ForJob(jobKey).StartNow().Build());

            SignallingJob.Fired.IsSet.Should().BeFalse("nothing is committed yet");

            await EndUnitOfWork(commit: true);
            EndDeferredScopes();

            // Comfortably below the two minute idle wait, so passing means the signal the scope raised as it
            // ended woke the scheduler, and the clock did not.
            SignallingJob.Fired.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue(
                "the signal followed the commit, so the scheduler found the trigger at once");

            await scheduler.Clear();
        }
        finally
        {
            await scheduler.Shutdown(true);
        }
    }

    [Test]
    public async Task AScopeReturnedAsItIsSignalsWithTheCallAndOneEndedAfterTheCommitSignalsThen()
    {
        ISchedulerSignaler signaler = A.Fake<ISchedulerSignaler>();
        IScheduler scheduler = await GetScheduler(EnlistApplicationTransaction, signaler);
        await scheduler.Clear();
        Fake.ClearRecordedCalls(signaler);

        await BeginUnitOfWork();
        await scheduler.ScheduleJob(CreateJob(), CreateTrigger());
        int signalledWithTheCall = SignalsReceivedBy(signaler);
        await EndUnitOfWork(commit: false);

        signalledWithTheCall.Should().BeGreaterThan(0,
            "disposing the enlistment as the call ends raises the signal the write deferred, before the commit");

        // The same write, with the source ending the enlistment itself once the application has committed.
        IScheduler deferring = await GetScheduler(DeferScopeUntilTheTestEnds, signaler);
        await deferring.Clear();
        Fake.ClearRecordedCalls(signaler);

        await BeginUnitOfWork();
        await deferring.ScheduleJob(CreateJob(), CreateTrigger());
        SignalsReceivedBy(signaler).Should().Be(0, "the enlistment has not ended, so the application has not committed");

        await EndUnitOfWork(commit: true);
        EndDeferredScopes();
        await WaitFor(() => SignalsReceivedBy(signaler) > 0);

        await deferring.Clear();
    }

    [Test]
    public async Task AReadThroughTheSourceSignalsNothing()
    {
        ISchedulerSignaler signaler = A.Fake<ISchedulerSignaler>();
        IScheduler scheduler = await GetScheduler(EnlistApplicationTransaction, signaler);
        await scheduler.Clear();
        Fake.ClearRecordedCalls(signaler);

        await BeginUnitOfWork();
        await scheduler.Exists(jobKey);
        await scheduler.GetJobDetail(jobKey);
        await scheduler.GetTrigger(new TriggerKey(jobKey.Name, jobKey.Group));
        await scheduler.QueryJobs(new JobQuery());
        await EndUnitOfWork(commit: true);

        SignalsReceivedBy(signaler).Should().Be(0, "a read changes no schedule, so there is nothing to tell the scheduler");
    }

    private IDisposable? EnlistApplicationTransaction(IScheduler scheduler)
    {
        return applicationTransaction is { } transaction ? scheduler.EnlistTransaction(transaction) : null;
    }

    private IDisposable? DeferScopeUntilTheTestEnds(IScheduler scheduler)
    {
        if (applicationTransaction is not { } transaction)
        {
            return null;
        }

        deferredScopes.Add(scheduler.EnlistTransaction(transaction));
        return new NothingToEnd();
    }

    private void EndDeferredScopes()
    {
        foreach (IDisposable scope in deferredScopes)
        {
            scope.Dispose();
        }

        deferredScopes.Clear();
    }

    private async Task BeginUnitOfWork()
    {
        applicationConnection = new NpgsqlConnection(TestConstants.PostgresConnectionString);
        await applicationConnection.OpenAsync();
        applicationTransaction = await applicationConnection.BeginTransactionAsync();
    }

    private async Task EndUnitOfWork(bool commit)
    {
        DbTransaction transaction = applicationTransaction!;
        applicationTransaction = null;

        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        await transaction.DisposeAsync();
        await applicationConnection!.DisposeAsync();
        applicationConnection = null;
    }

    private async Task<IScheduler> GetScheduler(
        Func<IScheduler, IDisposable?> source,
        ISchedulerSignaler? signaler = null,
        TimeSpan? idleWaitTime = null)
    {
        // One container at a time: a test builds a second scheduler over the same tables to compare the two
        // sources, and the first is not used again.
        if (container is not null)
        {
            await container.DisposeAsync();
        }

        ServiceCollection services = new();

        if (signaler is not null)
        {
            services.AddSingleton(signaler);
        }

        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = "one";

                if (idleWaitTime is { } idle)
                {
                    options.IdleWaitTime = idle;
                }
            });

            q.UsePersistentStore(store =>
            {
                store.UsePostgres(TestConstants.PostgresConnectionString);
                store.ConfigureStore(options =>
                {
                    options.AcceptEnlistedTransactions = true;
                    options.EnlistmentSource = context => source(context.Scheduler);
                });
            });
        });

        container = services.BuildServiceProvider();

        return await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    private static IJobDetail CreateJob()
    {
        return JobBuilder.Create<SignallingJob>().WithIdentity(jobKey).StoreDurably().Build();
    }

    private static ITrigger CreateTrigger()
    {
        return TriggerBuilder.Create()
            .WithIdentity(jobKey.Name, jobKey.Group)
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
    }

    private static int SignalsReceivedBy(ISchedulerSignaler signaler)
    {
        return Fake.GetCalls(signaler).Count(call => call.Method.Name == nameof(ISchedulerSignaler.SignalSchedulingChange));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(50);
        }

        condition().Should().BeTrue("the condition was not met within ten seconds");
    }

    private sealed class NothingToEnd : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [DisallowConcurrentExecution]
    public sealed class SignallingJob : IJob
    {
        internal static readonly ManualResetEventSlim Fired = new(false);

        internal static void Reset() => Fired.Reset();

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Fired.Set();
            return default;
        }
    }
}
