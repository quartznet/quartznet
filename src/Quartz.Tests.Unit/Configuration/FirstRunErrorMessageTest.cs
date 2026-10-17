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

using System.Collections.Specialized;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Calendar;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The errors a first run meets name what they are about and what to change, rather than leaving the
/// reader to find the registration they came from (#4095).
/// </summary>
/// <remarks>
/// Each keeps the exception type it always had, so code that catches one is unaffected.
/// </remarks>
public sealed class FirstRunErrorMessageTest
{
    [Test]
    public void ATriggerAddedWithoutItsJobNamesItselfAndTheFix()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.AddTrigger(t => t.WithIdentity("orphan").StartNow()));
        using ServiceProvider provider = services.BuildServiceProvider();

        Func<IReadOnlyList<ITrigger>> act = () => provider.ScheduledTriggers();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Trigger 'DEFAULT.orphan' hasn't been associated with a job.*",
                "the error is raised as the scheduler is built, far from the registration, so it has to say which one")
            .WithMessage("*.ForJob(...)*")
            .WithMessage("*ScheduleJob<T>(...)*");
    }

    [Test]
    public void ATriggerPointedAtAnotherJobNamesBothJobs()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ScheduleJob<NoOpJob>(
            t => t.WithIdentity("theTrigger").ForJob("someOtherJob").StartNow(),
            j => j.WithIdentity("theJob", "jobs")));
        using ServiceProvider provider = services.BuildServiceProvider();

        Func<IReadOnlyList<IJobDetail>> act = () => provider.ScheduledJobs();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Trigger 'DEFAULT.theTrigger' refers to job 'DEFAULT.someOtherJob', not to job 'jobs.theJob' being scheduled.*",
                "with both keys the reader sees which of the two is the typo")
            .WithMessage("*leave ForJob out*");
    }

    [Test]
    public async Task ANonDurableJobAddedWithoutATriggerNamesItselfAndBothWaysOut()
    {
        await using ServiceProvider provider = InMemoryContainer("non-durable");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Func<Task> act = async () => await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("lonely", "reports").Build());

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("Job 'reports.lonely' was added with no trigger, so it must be durable.*")
            .WithMessage("*StoreDurably()*")
            .WithMessage("*AddJobOptions.StoreNonDurableWhileAwaitingScheduling*");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task ANonDurableJobRegisteredWithoutATriggerNamesTheFix()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "registered-non-durable");
            q.AddJob<NoOpJob>(j => j.WithIdentity("lonely"));
        });
        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("A new job defined without any triggers must be durable: DEFAULT.lonely.*",
                "AddJob without StoreDurably is applied by the processor, which words this check itself")
            .WithMessage("*StoreDurably()*");
    }

    [Test]
    public async Task AJobThatExistsNamesTheOptionsThatReplaceIt()
    {
        await using ServiceProvider provider = InMemoryContainer("job-exists");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("import", "nightly").StoreDurably().Build();
        await scheduler.AddJob(job);

        Func<Task> act = async () => await scheduler.ScheduleJob(job, TriggerBuilder.Create().WithIdentity("first").StartNow().Build());

        (await act.Should().ThrowAsync<ObjectAlreadyExistsException>()
                .WithMessage("Unable to store Job: 'nightly.import', because one already exists with this identification.*",
                    "the old wording stays first, so a search for it still finds the new message")
                .WithMessage("*ScheduleJobOptions.Replacing*")
                .WithMessage("*AddJobOptions.Replacing*"))
            .Which.JobKey.Should().Be(job.Key, "the key the exception carries is unchanged");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task ATriggerThatExistsNamesTheOptionsThatReplaceOrKeepIt()
    {
        await using ServiceProvider provider = InMemoryContainer("trigger-exists");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("import").StoreDurably().Build();
        await scheduler.ScheduleJob(job, TriggerBuilder.Create().WithIdentity("hourly", "imports").ForJob(job).StartNow().Build());

        Func<Task> act = async () => await scheduler.ScheduleJob(
            TriggerBuilder.Create().WithIdentity("hourly", "imports").ForJob(job).StartNow().Build());

        (await act.Should().ThrowAsync<ObjectAlreadyExistsException>()
                .WithMessage("Unable to store Trigger: 'imports.hourly', because one already exists with this identification.*")
                .WithMessage("*ScheduleJobOptions.Replacing*")
                .WithMessage("*TriggerConflict.Keep*"))
            .Which.TriggerKey.Should().Be(new TriggerKey("hourly", "imports"));

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task ACalendarThatExistsInMemoryNamesTheOptionThatReplacesIt()
    {
        await using ServiceProvider provider = InMemoryContainer("calendar-exists");
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await AddingTheSameCalendarTwiceNamesTheOptionThatReplacesIt(scheduler);
    }

    [Test]
    public async Task ACalendarThatExistsInADatabaseNamesTheOptionThatReplacesIt()
    {
        using SqliteTestDatabase database = new("calendar-exists");
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "calendar-exists-ado");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await AddingTheSameCalendarTwiceNamesTheOptionThatReplacesIt(scheduler);
    }

    /// <summary>
    /// A description spelled as <c>quartz.dbprovider.*</c> keys names types Quartz knows no package for, so
    /// its failure keeps the old wording and adds what went wrong, which used to be only in the inner
    /// exception.
    /// </summary>
    [Test]
    public void AProviderDescribedByKeysThatCannotBeReadSaysWhy()
    {
        ConfigurationBasedDbMetadataFactory factory = new(
            new NameValueCollection { ["quartz.dbprovider.Acme.connectionType"] = "Acme.Data.AcmeConnection, Acme.Data" },
            "quartz.dbprovider");

        Action act = () => factory.GetDbMetadata("Acme");

        ArgumentException exception = act.Should().Throw<ArgumentException>()
            .WithMessage("Error while reading metadata information for provider 'Acme': *").Which;
        exception.InnerException.Should().NotBeNull();
        exception.Message.Should().Contain(exception.InnerException!.Message, "the cause is said in the message as well as kept");
    }

    private static async Task AddingTheSameCalendarTwiceNamesTheOptionThatReplacesIt(IScheduler scheduler)
    {
        await scheduler.AddCalendar("holidays", new HolidayCalendar());

        Func<Task> act = async () => await scheduler.AddCalendar("holidays", new HolidayCalendar());

        await act.Should().ThrowAsync<ObjectAlreadyExistsException>()
            .WithMessage("Calendar with name 'holidays' already exists.*")
            .WithMessage("*AddCalendarOptions.Replacing*");

        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    private static ServiceProvider InMemoryContainer(string instanceName)
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = instanceName));
        return services.BuildServiceProvider();
    }
}
