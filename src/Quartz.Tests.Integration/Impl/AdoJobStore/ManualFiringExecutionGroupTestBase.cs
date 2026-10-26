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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.Common;
using System.Threading.Tasks;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A manual firing on a database store takes the one execution group every stored trigger of its job
/// carries (#4092), as the triggers' rows hold it.
/// </summary>
/// <remarks>
/// <para>
/// The group is read from the triggers' <c>EXECUTION_GROUP</c> column in one statement rather than by
/// loading every trigger of the job (#4167). These tests pin that the answer is the one loading them gave:
/// groups compare ordinally, whatever the database's collation takes as equal, and a value written to the
/// column by hand is read as a trigger load reads it.
/// </para>
/// <para>
/// A scheduler on the dialect's database, never started, so the manual firings stay stored for the test to
/// read back. <see cref="SchedulerName" /> is the only isolation axis within a shared database.
/// </para>
/// </remarks>
[NonParallelizable]
public abstract class ManualFiringExecutionGroupTestBase
{
    protected const string SchedulerName = "manual-firing-execution-group";
    protected const string Reports = "reports";
    protected const string JobGroup = "manual-firing";

    private const string Batch = "batch";

    private readonly List<IScheduler> schedulers = new List<IScheduler>();

    /// <summary>The <c>quartz.dataSource.*.provider</c> of the dialect.</summary>
    protected abstract string Provider { get; }

    /// <summary>The database the dialect's tests run on.</summary>
    protected abstract string ConnectionString { get; }

    /// <summary>The driver delegate the scheduler is built with.</summary>
    protected abstract Type DriverDelegate { get; }

    /// <summary>A connection to <see cref="ConnectionString" />, for writing rows by hand and the clean-up.</summary>
    protected abstract DbConnection CreateConnection();

    /// <summary>Leaves an empty schema for <see cref="SchedulerName" /> behind.</summary>
    protected abstract Task PrepareDatabase();

    /// <summary>Removes what <see cref="SchedulerName" /> left in the database.</summary>
    protected abstract Task CleanUpDatabase();

    [SetUp]
    public Task SetUpDatabase()
    {
        return PrepareDatabase();
    }

    [TearDown]
    public async Task ShutDownAndCleanUp()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        schedulers.Clear();
        await CleanUpDatabase();
    }

    [Test]
    public async Task AManualFiringTakesTheGroupOnlyWhenEveryTriggerOfTheJobCarriesIt()
    {
        IScheduler scheduler = await Build();
        JobKey agreeing = await AddDurable(scheduler, "agreeing");
        await AddTrigger(scheduler, agreeing, "agreeing-1", Reports);
        await AddTrigger(scheduler, agreeing, "agreeing-2", Reports);
        JobKey mixed = await AddDurable(scheduler, "mixed");
        await AddTrigger(scheduler, mixed, "mixed-1", Reports);
        await AddTrigger(scheduler, mixed, "mixed-2", Batch);
        JobKey partly = await AddDurable(scheduler, "partly");
        await AddTrigger(scheduler, partly, "partly-1", Reports);
        await AddTrigger(scheduler, partly, "partly-2", executionGroup: null);
        JobKey cased = await AddDurable(scheduler, "cased");
        await AddTrigger(scheduler, cased, "cased-1", Reports);
        await AddTrigger(scheduler, cased, "cased-2", "Reports");
        JobKey untriggered = await AddDurable(scheduler, "untriggered");

        foreach (JobKey job in new[] { agreeing, mixed, partly, cased, untriggered })
        {
            await scheduler.TriggerJob(job);
        }

        (await GroupOfTheManualFiring(scheduler, agreeing)).Should().Be(Reports,
            "every trigger of the job carries reports, so that is the job's group");
        (await GroupOfTheManualFiring(scheduler, mixed)).Should().BeNull("two groups is no one group");
        (await GroupOfTheManualFiring(scheduler, partly)).Should().BeNull("a grouped and an ungrouped trigger is no one group");
        (await GroupOfTheManualFiring(scheduler, cased)).Should().BeNull(
            "groups compare ordinally, as execution limits count them, even where the database's collation takes the two names as equal");
        (await GroupOfTheManualFiring(scheduler, untriggered)).Should().BeNull("no trigger says anything");
    }

    /// <summary>
    /// The overload that takes a built trigger: a group the caller named is kept, and a trigger without one
    /// is given its job's.
    /// </summary>
    [Test]
    public async Task AnOperableTriggerKeepsItsOwnGroupAndIsGivenItsJobsWhenItHasNone()
    {
        IScheduler scheduler = await Build();
        JobKey job = await AddDurable(scheduler, "render");
        await AddTrigger(scheduler, job, "nightly", Reports);
        QuartzScheduler core = ((StdScheduler) scheduler).sched;

        IOperableTrigger named = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("named", JobGroup).ForJob(job).WithExecutionGroup(Batch).StartNow().Build();
        IOperableTrigger unnamed = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("unnamed", JobGroup).ForJob(job).StartNow().Build();

        // The unnamed one first: once the named one is stored, the job's triggers no longer share a group.
        await core.TriggerJob(unnamed);
        await core.TriggerJob(named);

        ((AbstractTrigger) await scheduler.GetTrigger(named.Key)).ExecutionGroup.Should().Be(Batch,
            "the caller named a group, and that is the firing's");
        ((AbstractTrigger) await scheduler.GetTrigger(unnamed.Key)).ExecutionGroup.Should().Be(Reports,
            "no group was named, so the one the job's triggers share is taken");
    }

    /// <summary>
    /// A group written to the column by hand rather than through a trigger is read as loading the trigger
    /// reads it: trimmed, and a blank one is no group.
    /// </summary>
    [Test]
    public async Task AGroupWrittenToTheColumnIsReadAsLoadingTheTriggerReadsIt()
    {
        IScheduler scheduler = await Build();
        JobKey padded = await AddDurable(scheduler, "padded");
        await AddTrigger(scheduler, padded, "padded-1", Reports);
        await AddTrigger(scheduler, padded, "padded-2", Reports);
        JobKey blank = await AddDurable(scheduler, "blank");
        await AddTrigger(scheduler, blank, "blank-1", Reports);
        await AddTrigger(scheduler, blank, "blank-2", Reports);
        JobKey empty = await AddDurable(scheduler, "empty");
        await AddTrigger(scheduler, empty, "empty-1", Reports);
        await AddTrigger(scheduler, empty, "empty-2", Reports);

        await WriteStoredGroup("padded-1", " " + Reports);
        await WriteStoredGroup("padded-2", Reports + "  ");
        await WriteStoredGroup("blank-2", "   ");
        await WriteStoredGroup("empty-2", "");

        foreach (JobKey job in new[] { padded, blank, empty })
        {
            await scheduler.TriggerJob(job);
        }

        (await GroupOfTheManualFiring(scheduler, padded)).Should().Be(Reports,
            "a trigger loaded from either row carries the name trimmed, so the two carry the same group");
        (await GroupOfTheManualFiring(scheduler, blank)).Should().BeNull("a trigger loaded from a blank name carries no group");
        (await GroupOfTheManualFiring(scheduler, empty)).Should().BeNull("a trigger loaded from an empty name carries no group");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>A scheduler on the dialect's database, never started.</summary>
    protected async Task<IScheduler> Build()
    {
        // A data source per scheduler: a scheduler's shutdown shuts its own down.
        string dataSource = "manual-firing-" + Guid.NewGuid().ToString("N");
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = SchedulerName,
            ["quartz.scheduler.instanceId"] = "node",
            ["quartz.threadPool.threadCount"] = "1",
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
            ["quartz.jobStore.driverDelegateType"] = DriverDelegate.AssemblyQualifiedName,
            ["quartz.jobStore.dataSource"] = dataSource,
            ["quartz.jobStore.tablePrefix"] = "QRTZ_",
            [$"quartz.dataSource.{dataSource}.provider"] = Provider,
            [$"quartz.dataSource.{dataSource}.connectionString"] = ConnectionString,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        IScheduler scheduler = await new StdSchedulerFactory(properties).GetScheduler();

        // The repository's lookup is name-only, so a scheduler left bound would be handed back to the
        // next test that asks for this name.
        SchedulerRepository.Instance.Remove(scheduler.SchedulerName, scheduler.SchedulerInstanceId);
        schedulers.Add(scheduler);
        return scheduler;
    }

    protected static async Task<JobKey> AddDurable(IScheduler scheduler, string name)
    {
        JobKey key = new JobKey(name, JobGroup);
        await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(key).StoreDurably().Build(), replace: false);
        return key;
    }

    /// <remarks>
    /// Due tomorrow, so it never fires; it is here for the group it carries.
    /// </remarks>
    protected static async Task AddTrigger(IScheduler scheduler, JobKey job, string name, string executionGroup)
    {
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(name, JobGroup)
            .ForJob(job)
            .WithExecutionGroup(executionGroup)
            .StartAt(DateTimeOffset.UtcNow.AddDays(1))
            .Build());
    }

    /// <summary>
    /// The execution group of the one manual firing stored for the job, whose trigger name starts with
    /// <c>MT_</c>, as loading it reads it.
    /// </summary>
    protected static async Task<string> GroupOfTheManualFiring(IScheduler scheduler, JobKey job)
    {
        IReadOnlyCollection<ITrigger> triggers = await scheduler.GetTriggersOfJob(job);
        ITrigger manual = triggers.Should().ContainSingle(
                trigger => trigger.Key.Name.StartsWith("MT_", StringComparison.Ordinal),
                "one manual firing of the job was stored")
            .Subject;

        return ((AbstractTrigger) manual).ExecutionGroup;
    }

    /// <summary>Writes <paramref name="executionGroup" /> to the trigger's row as it is, past the trigger's own normalization.</summary>
    private async Task WriteStoredGroup(string triggerName, string executionGroup)
    {
        using DbConnection connection = CreateConnection();
        await connection.OpenAsync();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE QRTZ_TRIGGERS SET EXECUTION_GROUP = @executionGroup WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName";
        AddParameter(command, "executionGroup", executionGroup);
        AddParameter(command, "schedulerName", SchedulerName);
        AddParameter(command, "triggerName", triggerName);
        (await command.ExecuteNonQueryAsync()).Should().Be(1, "the premise: the trigger's row is there to write to");
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
