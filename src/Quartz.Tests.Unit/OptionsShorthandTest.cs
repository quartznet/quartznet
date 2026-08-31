using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Jobs;
using Quartz.Tests;

namespace Quartz.Tests.Unit;

/// <summary>
/// The named statics on the option records. Each one abbreviates the object initializer nearly every
/// call that passes options at all was writing, and each has to keep meaning exactly that.
/// </summary>
public sealed class OptionsShorthandTest
{
    [Test]
    public void Replacing_IsTheInitializerItAbbreviates()
    {
        AddJobOptions.Replacing.Should().Be(new AddJobOptions { Replace = true },
            "the shorthand is the initializer, so it must not quietly set the other flag as well");
        ScheduleJobOptions.Replacing.Should().Be(new ScheduleJobOptions { Replace = true });
        AddTriggerOptions.Replacing.Should().Be(new AddTriggerOptions { Replace = true });
        AddCalendarOptions.Replacing.Should().Be(new AddCalendarOptions { Replace = true });
        AddCalendarOptions.ReplacingAndUpdatingTriggers.Should().Be(new AddCalendarOptions { Replace = true, UpdateTriggers = true });
    }

    /// <summary>
    /// <see cref="AddCalendarOptions" /> carries two flags, so the two shorthands have to stay
    /// distinguishable: replacing a calendar and re-pointing the triggers at it are separate decisions.
    /// </summary>
    [Test]
    public void ReplacingACalendarDoesNotTouchItsTriggersUnlessAsked()
    {
        AddCalendarOptions.Replacing.UpdateTriggers.Should().BeFalse(
            "leaving stored next-fire-times alone is the conservative default the record documents");
        AddCalendarOptions.ReplacingAndUpdatingTriggers.UpdateTriggers.Should().BeTrue();
    }

    /// <summary>
    /// Shape is not behaviour: the shorthand has to reach the store as the flag, which the negative
    /// half of this test is what proves — <see langword="default" /> still refuses to overwrite.
    /// </summary>
    [Test]
    public async Task AddJobReplacing_OverwritesTheStoredJob()
    {
        IScheduler scheduler = await NewScheduler("add-job-replacing");

        try
        {
            await scheduler.AddJob(Job("first"), AddJobOptions.Replacing);
            await scheduler.AddJob(Job("second"), AddJobOptions.Replacing);

            IJobDetail stored = await scheduler.GetJobDetail(new JobKey("compaction"));
            stored.Description.Should().Be("second", "AddJobOptions.Replacing carries Replace = true to the store");

            Func<Task> withoutTheOption = async () => await scheduler.AddJob(Job("third"));
            await withoutTheOption.Should().ThrowAsync<ObjectAlreadyExistsException>(
                "the default options replace nothing, so the shorthand is what made the two calls above work");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task AddCalendarReplacing_OverwritesTheStoredCalendar()
    {
        IScheduler scheduler = await NewScheduler("add-calendar-replacing");

        try
        {
            await scheduler.AddCalendar("holidays", new AnnualCalendar { Description = "first" }, AddCalendarOptions.Replacing);
            await scheduler.AddCalendar("holidays", new AnnualCalendar { Description = "second" }, AddCalendarOptions.Replacing);

            ICalendar stored = await scheduler.GetCalendar("holidays");
            stored.Description.Should().Be("second", "AddCalendarOptions.Replacing carries Replace = true to the store");

            Func<Task> withoutTheOption = async () => await scheduler.AddCalendar("holidays", new AnnualCalendar { Description = "third" });
            await withoutTheOption.Should().ThrowAsync<ObjectAlreadyExistsException>(
                "the default options replace nothing");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task ScheduleJobReplacing_OverwritesTheStoredPair()
    {
        IScheduler scheduler = await NewScheduler("schedule-job-replacing");

        try
        {
            await scheduler.ScheduleJob(Job("first"), [Trigger()], ScheduleJobOptions.Replacing);
            await scheduler.ScheduleJob(Job("second"), [Trigger()], ScheduleJobOptions.Replacing);

            IJobDetail stored = await scheduler.GetJobDetail(new JobKey("compaction"));
            stored.Description.Should().Be("second", "ScheduleJobOptions.Replacing carries Replace = true to the store");

            Func<Task> withoutTheOption = async () => await scheduler.ScheduleJob(Job("third"), [Trigger()]);
            await withoutTheOption.Should().ThrowAsync<ObjectAlreadyExistsException>(
                "the default options replace nothing");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// <see cref="AddTriggerOptions" /> has no scheduler-level call site — a trigger reaches a scheduler
    /// through <c>ScheduleJob</c> — so its shorthand is exercised where it is used, on the store.
    /// </summary>
    [Test]
    public async Task AddTriggerReplacing_OverwritesTheStoredTrigger()
    {
        IJobStore store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity());
        await store.AddJob(Job("owner"));

        await store.AddTrigger(OperableTrigger("first"), AddTriggerOptions.Replacing);
        await store.AddTrigger(OperableTrigger("second"), AddTriggerOptions.Replacing);

        IOperableTrigger stored = await store.GetTrigger(new TriggerKey("nightly"));
        stored.Description.Should().Be("second", "AddTriggerOptions.Replacing carries Replace = true to the store");

        Func<Task> withoutTheOption = async () => await store.AddTrigger(OperableTrigger("third"));
        await withoutTheOption.Should().ThrowAsync<ObjectAlreadyExistsException>(
            "the default options replace nothing, so the shorthand is what made the two calls above work");
    }

    private static IOperableTrigger OperableTrigger(string description)
    {
        return (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("nightly")
            .ForJob("compaction")
            .WithDescription(description)
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
    }

    private static IJobDetail Job(string description)
    {
        return JobBuilder.Create<NoOpJob>()
            .WithIdentity("compaction")
            .WithDescription(description)
            .StoreDurably()
            .Build();
    }

    private static ITrigger Trigger()
    {
        return TriggerBuilder.Create()
            .WithIdentity("nightly")
            .ForJob("compaction")
            .StartAt(DateTimeOffset.UtcNow.AddHours(1))
            .Build();
    }

    private static ValueTask<IScheduler> NewScheduler(string name)
    {
        return QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = name)
                .UseInMemoryStore())
            .BuildScheduler();
    }
}
