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

using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

/// <summary>
/// A job that fails, retried under whichever policy wins — the trigger's own, the job type's
/// <see cref="RetryPolicyAttribute" /> or the scheduler's default — through a running scheduler on the
/// in-memory store and on a SQLite one.
/// </summary>
/// <remarks>
/// <para>
/// Each firing records the policy it ran under and the policy its own trigger carried. The second is
/// the store's row read back on every retry, so its staying empty is what says an inherited policy is
/// never written into <c>RETRY_POLICY</c>.
/// </para>
/// <para>
/// The waits are a quarter of a second, and nothing fakes a clock: the point is that the scheduling loop
/// picks each retry up on its own.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class RetryPolicyInheritanceExecutionTest
{
    private static readonly RetryPolicy declared = RetryPolicy.Fixed(2, TimeSpan.FromMilliseconds(250));
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Long enough for one more retry to have fired had anything scheduled one.
    /// </summary>
    private static readonly TimeSpan settle = TimeSpan.FromMilliseconds(1500);

    private static readonly ConcurrentDictionary<string, Recorder> recorders = new(StringComparer.Ordinal);

    private readonly List<IScheduler> schedulers = [];
    private readonly List<ServiceProvider> providers = [];
    private SqliteTestDatabase database;

    public enum Store
    {
        InMemory,
        Sqlite
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        foreach (ServiceProvider provider in providers)
        {
            await provider.DisposeAsync();
        }

        schedulers.Clear();
        providers.Clear();
        recorders.Clear();
        database?.Dispose();
        database = null;
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AJobTypesPolicyRetriesATriggerThatNamesNone(Store store)
    {
        Recorder recorder = Record("declared", failures: int.MaxValue);
        ExhaustedListener exhausted = new();

        IScheduler scheduler = await Build(store, q => q.AddTriggerListener(exhausted));
        await ScheduleOneShot<DeclaringJob>(scheduler, "declared");
        await scheduler.Start();

        await recorder.WaitFor(3);
        await Task.Delay(settle);

        recorder.Firings.Should().HaveCount(3, "one regular fire and the two retries the job type's policy allows");
        recorder.Firings.Select(x => x.RetryAttempt).Should().Equal([0, 1, 2]);
        recorder.Firings.Select(x => x.Effective).Should().AllBeEquivalentTo(declared,
            "the context reports the policy the firing is retried under, which here is the job type's");
        recorder.Firings.Select(x => x.TriggersOwn).Should().AllSatisfy(x => x.Should().BeNull(
            "the trigger names no policy, and the job type's is looked up rather than written onto its row"));

        exhausted.Heard.Should().ContainSingle("an occurrence that ran out of inherited retries says so, as one that ran out of its own does")
            .Which.Should().Be((declared, 2));
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task TheSchedulersDefaultRetriesATriggerAndAJobTypeThatNameNone(Store store)
    {
        RetryPolicy fallback = RetryPolicy.Fixed(1, TimeSpan.FromMilliseconds(250));
        Recorder recorder = Record("plain", failures: int.MaxValue);

        IScheduler scheduler = await Build(store, q => q.UseDefaultRetryPolicy(fallback));
        await ScheduleOneShot<PlainJob>(scheduler, "plain");
        await scheduler.Start();

        await recorder.WaitFor(2);
        await Task.Delay(settle);

        recorder.Firings.Should().HaveCount(2, "one regular fire and the one retry the default allows");
        recorder.Firings.Select(x => x.Effective).Should().AllBeEquivalentTo(fallback);
        recorder.Firings.Select(x => x.TriggersOwn).Should().AllSatisfy(x => x.Should().BeNull());
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task ATriggersOwnPolicyBeatsTheJobTypes(Store store)
    {
        RetryPolicy own = RetryPolicy.Fixed(1, TimeSpan.FromMilliseconds(250));
        Recorder recorder = Record("declared", failures: int.MaxValue);

        IScheduler scheduler = await Build(store, q => q.UseDefaultRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(250))));
        await ScheduleOneShot<DeclaringJob>(scheduler, "declared", own);
        await scheduler.Start();

        await recorder.WaitFor(2);
        await Task.Delay(settle);

        recorder.Firings.Should().HaveCount(2, "the trigger's one retry, not the job type's two or the default's three");
        recorder.Firings.Select(x => x.Effective).Should().AllBeEquivalentTo(own);
        recorder.Firings.Select(x => x.TriggersOwn).Should().AllBeEquivalentTo(own);
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AJobTypesPolicyBeatsTheDefault(Store store)
    {
        Recorder recorder = Record("declared", failures: int.MaxValue);

        IScheduler scheduler = await Build(store, q => q.UseDefaultRetryPolicy(RetryPolicy.Fixed(4, TimeSpan.FromMilliseconds(250))));
        await ScheduleOneShot<DeclaringJob>(scheduler, "declared");
        await scheduler.Start();

        await recorder.WaitFor(3);
        await Task.Delay(settle);

        recorder.Firings.Should().HaveCount(3, "the job type's two retries, not the default's four");
        recorder.Firings.Select(x => x.Effective).Should().AllBeEquivalentTo(declared);
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task ATriggerGivenNoneIsNotRetriedWhateverItWouldInherit(Store store)
    {
        Recorder recorder = Record("declared", failures: int.MaxValue);
        ExhaustedListener exhausted = new();

        IScheduler scheduler = await Build(store, q => q
            .UseDefaultRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMilliseconds(250)))
            .AddTriggerListener(exhausted));
        TriggerKey key = await ScheduleOneShot<DeclaringJob>(scheduler, "declared", RetryPolicy.None);
        if (store == Store.Sqlite)
        {
            (await StoredRetryPolicy(key)).Should().Be("none",
                "an empty column is a trigger that inherits, so the opt-out needs a value of its own");
        }

        await scheduler.Start();

        await recorder.WaitFor(1);
        await Task.Delay(settle);

        recorder.Firings.Should().ContainSingle("None refuses both the job type's policy and the default");
        recorder.Firings.Single().Effective.Should().BeNull();
        recorder.Firings.Single().TriggersOwn.Should().BeSameAs(RetryPolicy.None,
            "the opt-out is stored with the trigger and read back as itself, not as a trigger with no policy");
        exhausted.Heard.Should().BeEmpty("no policy applied, so there were no retries to run out of");
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AJobTypeDeclaringNoneIsNotRetriedUnderTheDefault(Store store)
    {
        Recorder recorder = Record("never", failures: int.MaxValue);

        IScheduler scheduler = await Build(store, q => q.UseDefaultRetryPolicy(RetryPolicy.Fixed(2, TimeSpan.FromMilliseconds(250))));
        await ScheduleOneShot<NeverRetriedJob>(scheduler, "never");
        await scheduler.Start();

        await recorder.WaitFor(1);
        await Task.Delay(settle);

        recorder.Firings.Should().ContainSingle("[RetryPolicy(0)] exempts the job type from the scheduler's default");
    }

    /// <summary>
    /// A failure answered with another attempt settles nothing, whoever's policy it was: the continuation
    /// waiting for the parent's success is released by the retry that succeeds rather than discarded by
    /// the first failure.
    /// </summary>
    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task AnInheritedRetryHoldsAContinuationAsATriggersOwnDoes(Store store)
    {
        Recorder parent = Record("parent", failures: 1);
        Recorder child = Record("child", failures: 0);

        IScheduler scheduler = await Build(store, _ => { });
        TriggerKey parentKey = await ScheduleOneShot<DeclaringJob>(scheduler, "parent");
        await scheduler.ScheduleJob(
            JobBuilder.Create<PlainJob>().WithIdentity("child", "retries").Build(),
            TriggerBuilder.Create()
                .WithIdentity("child", "retries")
                .StartNow()
                .StartAfter(parentKey, ContinuationCondition.OnSuccess)
                .Build());
        await scheduler.Start();

        await parent.WaitFor(2);
        await child.WaitFor(1);

        parent.Firings.Select(x => x.RetryAttempt).Should().Equal([0, 1]);
        child.Firings.Should().ContainSingle("the parent's occurrence succeeded on its retry, which is what the continuation waited for");
    }

    [TestCase(Store.InMemory)]
    [TestCase(Store.Sqlite)]
    public async Task ADefaultPolicyRetryIsRecordedInTheHistoryAsATriggersIs(Store store)
    {
        Recorder recorder = Record("plain", failures: int.MaxValue);

        IScheduler scheduler = await Build(
            store,
            q => q.UseDefaultRetryPolicy(RetryPolicy.Fixed(1, TimeSpan.FromMilliseconds(250))),
            services => services.AddQuartzExecutionHistory());
        await ScheduleOneShot<PlainJob>(scheduler, "plain");
        await scheduler.Start();

        await recorder.WaitFor(2);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        IExecutionHistoryStore history = providers[^1].GetRequiredService<IExecutionHistoryStore>();
        List<ExecutionHistoryEntry> rows = [.. (await history.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName
        })).Items.OrderBy(x => x.RetryAttempt)];

        rows.Should().HaveCount(2);
        rows.Select(x => x.Succeeded).Should().AllBeEquivalentTo(false);
        rows.Select(x => x.RetryAttempt).Should().Equal([0, 1]);
        rows.Select(x => x.RetryScheduled).Should().Equal([true, false],
            "the first failure was answered with another attempt and the second was not, exactly as under a trigger's "
            + "own policy, so FailedFinally selects the second row alone");
    }

    /// <summary>
    /// A row a 4.3 node wrote has no policy in <c>RETRY_POLICY</c>. Nothing rewrites it on upgrade, and
    /// nothing needs to: the default is looked up as the firing fails.
    /// </summary>
    [Test]
    public async Task ATriggerStoredBeforeTheDefaultExistedIsCoveredByIt()
    {
        string instanceName = "retry-inheritance-" + Guid.NewGuid().ToString("N");
        Recorder recorder = Record("plain", failures: int.MaxValue);

        IScheduler before = await Build(Store.Sqlite, _ => { }, instanceName: instanceName);
        TriggerKey key = await ScheduleOneShot<PlainJob>(before, "plain");
        await before.Shutdown();

        (await StoredRetryPolicy(key)).Should().BeNull("the trigger was stored with no policy of its own, as every 4.3 trigger without one was");

        IScheduler after = await Build(Store.Sqlite, q => q.UseDefaultRetryPolicy(RetryPolicy.Fixed(1, TimeSpan.FromMilliseconds(250))), instanceName: instanceName);
        await after.Start();

        await recorder.WaitFor(2);
        await Task.Delay(settle);

        recorder.Firings.Should().HaveCount(2, "the stored trigger is retried under a default it was stored without");
        recorder.Firings.Select(x => x.TriggersOwn).Should().AllSatisfy(x => x.Should().BeNull(
            "the retry read the row back with RETRY_POLICY still empty: the default is never back-filled"));
    }

    private Recorder Record(string jobName, int failures)
    {
        Recorder recorder = new(failures);
        recorders[jobName] = recorder;
        return recorder;
    }

    private static async Task<TriggerKey> ScheduleOneShot<TJob>(IScheduler scheduler, string name, RetryPolicy triggerPolicy = null) where TJob : IJob
    {
        TriggerKey key = new(name, "retries");

        // Exactly one scheduled occurrence, so every firing after the first is a retry and nothing else.
        await scheduler.ScheduleJob(
            JobBuilder.Create<TJob>().WithIdentity(name, "retries").Build(),
            TriggerBuilder.Create()
                .WithIdentity(key)
                .StartNow()
                .WithRetryPolicy(triggerPolicy)
                .Build());

        return key;
    }

    private async Task<string> StoredRetryPolicy(TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT RETRY_POLICY FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        object value = await command.ExecuteScalarAsync();
        value.Should().NotBeNull("the trigger's row is there to read");
        return value is DBNull ? null : (string) value;
    }

    private async Task<IScheduler> Build(
        Store store,
        Action<IQuartzBuilder> configure,
        Action<IServiceCollection> services = null,
        string instanceName = null)
    {
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = instanceName ?? "retry-inheritance-" + Guid.NewGuid().ToString("N");
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });

            if (store == Store.Sqlite)
            {
                database ??= new SqliteTestDatabase("retry-inheritance");
                string connectionString = database.ConnectionString;
                q.UsePersistentStore(persistent =>
                {
                    persistent.UseSqlite(SqliteFactory.Instance, connectionString);
                    persistent.ProvisionSchema();
                });
            }
            else
            {
                q.UseInMemoryStore();
            }

            configure(q);
        });
        services?.Invoke(collection);

        ServiceProvider provider = collection.BuildServiceProvider();
        providers.Add(provider);

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        schedulers.Add(scheduler);
        return scheduler;
    }

    private sealed record Firing(int RetryAttempt, RetryPolicy Effective, RetryPolicy TriggersOwn);

    private sealed class Recorder(int failures)
    {
        private readonly SemaphoreSlim fired = new(0);
        private int remainingFailures = failures;

        public ConcurrentQueue<Firing> Firings { get; } = new();

        /// <summary>Records the firing, and answers whether it should fail.</summary>
        public bool Fire(IJobExecutionContext context)
        {
            Firings.Enqueue(new Firing(context.RetryAttempt, context.RetryPolicy, context.Trigger.RetryPolicy));
            bool fail = Interlocked.Decrement(ref remainingFailures) >= 0;
            fired.Release();
            return fail;
        }

        public async Task WaitFor(int firings)
        {
            for (int i = 0; i < firings; i++)
            {
                (await fired.WaitAsync(waitLimit)).Should().BeTrue("firing {0} of {1} should have happened by now", i + 1, firings);
            }
        }
    }

    private abstract class RecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (recorders[context.JobDetail.Key.Name].Fire(context))
            {
                throw new InvalidOperationException("the upstream system is down");
            }

            return default;
        }
    }

    private sealed class PlainJob : RecordingJob;

    [RetryPolicy(2, "00:00:00.250")]
    private sealed class DeclaringJob : RecordingJob;

    [RetryPolicy(0)]
    private sealed class NeverRetriedJob : RecordingJob;

    private sealed class ExhaustedListener : ITriggerListener
    {
        public ConcurrentQueue<(RetryPolicy Policy, int RetryAttempt)> Heard { get; } = new();

        public string Name => "retry-inheritance-exhausted";

        public ValueTask TriggerRetriesExhausted(
            ITrigger trigger,
            IJobExecutionContext context,
            JobExecutionException exception,
            CancellationToken cancellationToken = default)
        {
            Heard.Enqueue((context.RetryPolicy, context.RetryAttempt));
            return default;
        }
    }
}
