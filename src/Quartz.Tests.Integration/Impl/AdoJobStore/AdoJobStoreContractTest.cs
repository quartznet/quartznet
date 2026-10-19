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

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The job store contract as the ADO.NET store implements it, against one real database. A subclass
/// per dialect says which driver, which delegate and which database, and the contract itself is the
/// same assertions the in-memory store answers.
/// </summary>
/// <remarks>
/// <para>
/// The dialects are where an ADO store's answers can diverge from each other: paging syntax, LIKE
/// escaping, how a boolean and a timestamp are spelled, and which statements a provider binds
/// positionally. Running the contract against one dialect proves the store's logic; running it
/// against all of them is what proves the SQL.
/// </para>
/// <para>
/// Three pieces of fixture discipline matter and are shared here, because getting any of them wrong
/// makes a green run meaningless:
/// </para>
/// <list type="bullet">
/// <item>
/// Every row is keyed by scheduler name, so each fixture uses one of its own. Two dialect fixtures
/// that shared a name would be invisible to each other only until the same database served both.
/// </item>
/// <item>
/// The store is cleared as it is built, which is once per test. A container's database outlives the
/// test that wrote to it, so anything the previous test stored would still be there to be counted.
/// </item>
/// <item>
/// <see cref="IJobStore.SchedulerStarted" /> is never called. It spawns the misfire handler, which
/// moves trigger state between a test's arrange and its act — #3303 is what that looks like when it
/// happens — and leaves a foreground thread behind for every store the fixture builds.
/// </item>
/// </list>
/// </remarks>
public abstract class AdoJobStoreContractTest : JobStoreContractTest
{
    private const string DataSourceName = "job-store-contract";
    private const string InstanceId = "contract-instance";

    protected override string StoreInstanceId => InstanceId;

    /// <summary>
    /// The scheduler name every row this fixture writes is keyed by. Derived from the fixture's own
    /// type name, so no two dialect fixtures can see each other's rows even in one database.
    /// </summary>
    protected string SchedulerName => "JobStoreContract_" + GetType().Name;

    /// <summary>
    /// The Quartz provider name of the ADO.NET driver, as <c>quartz.dataSource.default.provider</c>
    /// spells it.
    /// </summary>
    protected abstract string DbProviderName { get; }

    /// <summary>
    /// Makes the database ready and answers the connection string to reach it with. The container
    /// fixtures only read the connection string their container published; the file-database ones
    /// create the file here.
    /// </summary>
    protected abstract ValueTask<string> PrepareDatabase();

    /// <summary>
    /// The delegate that speaks this database's SQL dialect — the same one
    /// <c>UsePostgres</c>, <c>UseSqlServer</c> and their siblings select.
    /// </summary>
    protected abstract IDriverDelegate CreateDriverDelegate();

    /// <summary>
    /// The connection string <see cref="TestcontainersDatabaseEnvironment" /> published for this
    /// database when it started the container, read from the environment variable it publishes it in.
    /// </summary>
    /// <remarks>
    /// Read rather than defaulted, and deliberately so. The older ADO fixtures each carry a
    /// hard-coded localhost fallback, from when these databases were started by hand outside the test
    /// run; a container is now the only supported way to get a <c>db-*</c> leg going, so a fallback
    /// here would buy nothing and cost the one thing worth having — when the container fails to
    /// start, this says so, instead of timing out against whatever else happens to be on localhost.
    /// Not carrying credentials of its own is the other half of the point.
    /// </remarks>
    protected static string ContainerConnectionString(string variableName)
    {
        string connectionString = Environment.GetEnvironmentVariable(variableName);

        connectionString.Should().NotBeNullOrWhiteSpace(
            "{0} is set by the container this assembly starts, so an empty one means the container "
            + "for this leg never started — run the fixture through its own QUARTZ_TEST_DATABASE leg",
            variableName);

        return connectionString;
    }

    /// <summary>
    /// Builds the store under test. Overridden by the fixture that runs the contract against
    /// <see cref="ExternalTransactionJobStore" />.
    /// </summary>
    /// <param name="dbProvider">The provider reaching the database this fixture prepared.</param>
    /// <param name="driverDelegate">The dialect's delegate.</param>
    /// <param name="storeJobDataAsStrings">
    /// Whether the store writes job data as name-value pairs. Only the one case about that setting
    /// passes <see langword="true" />; everything else runs against the default.
    /// </param>
    private protected virtual AdoJobStoreBase CreateJobStore(
        IDbProvider dbProvider,
        IDriverDelegate driverDelegate,
        bool storeJobDataAsStrings = false)
    {
        return new LocalTransactionJobStore(StoreDependencies(dbProvider, driverDelegate, storeJobDataAsStrings));
    }

    /// <summary>
    /// What the store under test is built from. Deliberately no lock handler: the store picks the one
    /// its delegate and clustering settings call for, which is the decision production makes too — the
    /// configuration builder injects one only when the application asked for a specific one. SQLite
    /// therefore gets SqliteLockHandler here exactly as it would in an application.
    /// </summary>
    private protected AdoJobStoreDependencies StoreDependencies(
        IDbProvider dbProvider,
        IDriverDelegate driverDelegate,
        bool storeJobDataAsStrings = false)
    {
        return TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, InstanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName, configure: options => options.StoreJobDataAsStrings = storeJobDataAsStrings),
            dbProvider: dbProvider,
            driverDelegate: driverDelegate) with
        {
            LockHandler = null,
        };
    }

    /// <summary>
    /// The database this fixture's store was built against, kept so that a second store can be built
    /// over the same rows rather than over a fresh file.
    /// </summary>
    private string preparedConnectionString;

    protected override async ValueTask<IJobStore> CreateStore()
    {
        preparedConnectionString = await PrepareDatabase();

        // The store reads through the provider it is constructed with, so the test only has to build
        // one.
        IDbProvider dbProvider = new DbProvider(DbProviderName, preparedConnectionString);

        AdoJobStoreBase store = CreateJobStore(dbProvider, CreateDriverDelegate());

        await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = InstanceId });

        // Whatever the previous test left behind is this scheduler name's rows, and nothing else is.
        await store.Clear();

        return store;
    }

    /// <summary>
    /// A second store over the same database with <see cref="AdoJobStoreOptions.StoreJobDataAsStrings" />
    /// on, which is the delegate's <c>useProperties</c> and the setting the tutorial recommends.
    /// </summary>
    /// <remarks>
    /// Built from the connection string the fixture already prepared rather than by preparing another
    /// one — for a container that would be the same database anyway, and for the SQLite fixtures it
    /// would be a second file that nothing deletes. Deliberately not cleared either: it shares this
    /// fixture's scheduler name, and the store the fixture built cleared those rows as it was built.
    /// </remarks>
    protected override async ValueTask<IJobStore> CreateStoreWritingJobDataAsStrings()
    {
        IDbProvider dbProvider = new DbProvider(DbProviderName, preparedConnectionString);

        AdoJobStoreBase store = CreateJobStore(dbProvider, CreateDriverDelegate(), storeJobDataAsStrings: true);

        await store.Initialize(new SchedulerIdentity { SchedulerName = SchedulerName, InstanceId = InstanceId });

        return store;
    }

    /// <summary>
    /// A trigger the application declares is re-applied by every scheduler created over the store, and the
    /// re-applied trigger keeps the time it last fired, so the first firing after a restart reports the one
    /// before it (#1834, #4122).
    /// </summary>
    /// <remarks>
    /// Here rather than in the contract the in-memory store answers too: it takes a scheduler, and a
    /// restart only means something over rows that outlive one. The unit suite has the in-memory case.
    /// </remarks>
    [Test]
    public async Task ADeclaredTriggerReappliedAtStartupKeepsItsPreviousFireTime()
    {
        JobKey jobKey = new("declared", JobGroupA);
        TriggerKey triggerKey = new("declared", TriggerGroupA);

        await CreateASchedulerDeclaring(jobKey, triggerKey);

        TriggerFiredBundle firstFiring = await FireAndComplete(triggerKey);
        firstFiring.PreviousFireTimeUtc.Should().BeNull("the premise: a new trigger's first firing has no previous one");

        DateTimeOffset? previous = (await Store.GetTrigger(triggerKey)).PreviousFireTimeUtc;
        previous.Should().NotBeNull("the premise: a trigger that has fired knows when it last fired");

        await CreateASchedulerDeclaring(jobKey, triggerKey);

        (await Store.GetTrigger(triggerKey)).PreviousFireTimeUtc.Should().Be(previous,
            "creating a scheduler re-applies the declared trigger, and re-applying the same trigger must not "
            + "forget that it has fired");

        TriggerFiredBundle nextFiring = await FireAndComplete(triggerKey);
        nextFiring.PreviousFireTimeUtc.Should().Be(previous,
            "context.PreviousFireTimeUtc on the first firing after a restart is the firing before the restart");
    }

    /// <summary>
    /// The rest of a declared trigger's run state survives a start the same way: how many times it has
    /// fired, so a finite repeat count is not run again (#4128).
    /// </summary>
    [Test]
    public async Task ADeclaredTriggerReappliedAtStartupKeepsItsFireCount()
    {
        JobKey jobKey = new("counted", JobGroupA);
        TriggerKey triggerKey = new("counted", TriggerGroupA);

        await CreateASchedulerDeclaring(jobKey, triggerKey, FourFiringsAMinuteApart);
        (await FireOnce(triggerKey)).Should().BeTrue();
        (await FireOnce(triggerKey)).Should().BeTrue();

        await CreateASchedulerDeclaring(jobKey, triggerKey, FourFiringsAMinuteApart);

        ((ISimpleTrigger) await Store.GetTrigger(triggerKey)).TimesTriggered.Should().Be(2,
            "the trigger fired twice before the start, and the start re-applies the same trigger");
        (await FireUntilDone(triggerKey)).Should().Be(2,
            "a repeat count of three is four firings in all, two of which happened before the start");
    }

    /// <summary>
    /// A changed schedule wins: the declaration says something new, and its trigger starts fresh (#4128).
    /// </summary>
    [Test]
    public async Task ADeclaredTriggerWithAChangedRepeatCountStartsFreshAtStartup()
    {
        JobKey jobKey = new("recounted", JobGroupA);
        TriggerKey triggerKey = new("recounted", TriggerGroupA);

        await CreateASchedulerDeclaring(jobKey, triggerKey, FourFiringsAMinuteApart);
        (await FireOnce(triggerKey)).Should().BeTrue();
        (await FireOnce(triggerKey)).Should().BeTrue();

        await CreateASchedulerDeclaring(jobKey, triggerKey, t => t.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(1)).WithRepeatCount(5)));

        ((ISimpleTrigger) await Store.GetTrigger(triggerKey)).TimesTriggered.Should().Be(0, "a changed schedule is a new trigger");
        (await FireUntilDone(triggerKey)).Should().Be(6, "a repeat count of five is six firings, all of them still to come");
    }

    /// <summary>
    /// A retry the trigger is waiting on at shutdown is still waiting after the start, at its time (#4128).
    /// </summary>
    [Test]
    public async Task ADeclaredTriggerWaitingOnARetryKeepsItAtStartup()
    {
        JobKey jobKey = new("retrying", JobGroupA);
        TriggerKey triggerKey = new("retrying", TriggerGroupA);
        Action<ITriggerConfigurator<IJob>> retrying = t => t
            .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(10)));

        await CreateASchedulerDeclaring(jobKey, triggerKey, retrying);
        (await FireOnce(triggerKey, new JobExecutionException(new InvalidOperationException("transient")))).Should().BeTrue();

        IOperableTrigger beforeStart = await Store.GetTrigger(triggerKey);
        beforeStart.RetryAttempt.Should().Be(1, "the premise: the failed firing scheduled a retry");

        await CreateASchedulerDeclaring(jobKey, triggerKey, retrying);

        IOperableTrigger afterStart = await Store.GetTrigger(triggerKey);
        afterStart.RetryAttempt.Should().Be(1, "the retry pending at shutdown is still pending");
        afterStart.NextFireTimeUtc.Should().Be(beforeStart.NextFireTimeUtc, "a retry pending at shutdown still happens at its time");
    }

    /// <summary>
    /// A trigger an operator paused on its own stays paused, with its record, when a start re-applies it
    /// (#4128).
    /// </summary>
    [Test]
    public async Task ADeclaredTriggerPausedOnItsOwnStaysPausedAtStartup()
    {
        JobKey jobKey = new("paused", JobGroupA);
        TriggerKey triggerKey = new("paused", TriggerGroupA);

        await CreateASchedulerDeclaring(jobKey, triggerKey);
        (await Store.PauseTriggerWith(triggerKey, new PauseDetails { Reason = "maintenance window", RequestedBy = "ops" })).Should().BeTrue();
        PauseInfo pause = await Store.GetTriggerPause(triggerKey);

        await CreateASchedulerDeclaring(jobKey, triggerKey);

        (await Store.GetTriggerState(triggerKey)).Should().Be(TriggerState.Paused, "a redeploy is not somebody resuming the trigger");
        (await Store.GetTriggerPause(triggerKey)).Should().Be(pause, "the pause is the one the operator made, not a new one");
    }

    private static void FourFiringsAMinuteApart(ITriggerConfigurator<IJob> trigger)
    {
        trigger.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(1)).WithRepeatCount(3));
    }

    /// <summary>
    /// What a process start does to this fixture's rows: a scheduler of the same name over a store of its
    /// own, which applies the job and trigger the application declares as it is created. Never started, and
    /// shut down again, so the firings stay the test's to drive.
    /// </summary>
    private async ValueTask CreateASchedulerDeclaring(JobKey jobKey, TriggerKey triggerKey, Action<ITriggerConfigurator<IJob>> schedule = null)
    {
        AdoJobStoreBase store = CreateJobStore(new DbProvider(DbProviderName, preparedConnectionString), CreateDriverDelegate());

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options =>
                {
                    options.InstanceName = SchedulerName;
                    options.InstanceId = InstanceId;
                });
                q.UseJobStore(store);
                q.AddJob<ContractTestJob>(job => job.WithIdentity(jobKey));
                q.AddTrigger(trigger =>
                {
                    trigger.WithIdentity(triggerKey).ForJob(jobKey).StartNow();

                    if (schedule is null)
                    {
                        trigger.WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever());
                    }
                    else
                    {
                        schedule(trigger);
                    }
                });
            })
            .BuildScheduler();

        await scheduler.Shutdown();
    }

    /// <summary>
    /// Fires the trigger at its next fire time within the hour, completing the firing the way the run
    /// shell does: the trigger decides what comes next. Answers whether there was a firing to make.
    /// </summary>
    private async ValueTask<bool> FireOnce(TriggerKey triggerKey, JobExecutionException failure = null)
    {
        List<IOperableTrigger> acquired = await Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddHours(1),
            MaxCount = 1,
        });

        if (acquired.Count == 0)
        {
            return false;
        }

        acquired.Should().ContainSingle(x => x.Key.Equals(triggerKey), "the declared trigger is the only one stored");

        List<TriggerFiredResult> fired = await Store.TriggersFired(acquired);
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Which.TriggerFiredBundle;
        bundle.Should().NotBeNull("the trigger is due and nothing stands in its way");

        // The bundle's trigger, as the run shell's: the store advanced a copy of its own.
        SchedulerInstruction instruction = bundle.Trigger.ExecutionComplete(FakeItEasy.A.Fake<IJobExecutionContext>(), failure);
        await Store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, instruction);
        return true;
    }

    private async ValueTask<int> FireUntilDone(TriggerKey triggerKey)
    {
        const int Ceiling = 20;

        for (int fired = 0; fired < Ceiling; fired++)
        {
            if (!await FireOnce(triggerKey))
            {
                return fired;
            }
        }

        return Ceiling;
    }

    private async ValueTask<TriggerFiredBundle> FireAndComplete(TriggerKey triggerKey)
    {
        List<IOperableTrigger> acquired = await Store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddMinutes(1),
            MaxCount = 1,
        });

        IOperableTrigger firing = acquired.Should().ContainSingle(x => x.Key.Equals(triggerKey),
            "the declared trigger starts now and is the only one due").Subject;

        List<TriggerFiredResult> fired = await Store.TriggersFired([firing]);
        TriggerFiredBundle bundle = fired.Should().ContainSingle().Which.TriggerFiredBundle;
        bundle.Should().NotBeNull("the trigger is due and nothing stands in its way");

        await Store.TriggeredJobComplete(firing, bundle.JobDetail, SchedulerInstruction.NoInstruction);
        return bundle;
    }
}
