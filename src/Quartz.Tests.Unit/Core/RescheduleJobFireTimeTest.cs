#nullable enable

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What rescheduling does with a trigger instance that already carries fire times — the shape a caller
/// is in after reading a trigger, changing something on it and handing the same object back, and the
/// one <see cref="Quartz.Xml.XmlSchedulingDataProcessor" /> itself is in.
/// </summary>
/// <remarks>
/// A next fire time the caller set is kept, which is what the clone in
/// <see cref="Quartz.Core.QuartzScheduler.RescheduleJob" /> is for. One left behind by the start time
/// rescheduling advances is not: no job store recomputes anything, both of them store the trigger as
/// handed over, and a simple trigger answers "the start time" for any fire time asked for before it —
/// so a trigger stored with a next fire time behind its own start fires at both of them, milliseconds
/// apart (#3554).
/// </remarks>
public sealed class RescheduleJobFireTimeTest
{
    private static readonly DateTimeOffset start = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan interval = TimeSpan.FromMinutes(1);

    [Test]
    public async Task AFireTimeLeftBehindByTheAdvancedStartTimeIsRecomputed()
    {
        FakeTimeProvider clock = new FakeTimeProvider(start);
        await using StandaloneSchedulerFactory factory = CreateFactory(clock, "reschedule-recomputes");
        IScheduler scheduler = await factory.GetScheduler();

        ITrigger trigger = RepeatingTrigger(clock);
        await scheduler.ScheduleJob(Job(), trigger);
        trigger.NextFireTimeUtc.Should().Be(start, "scheduling a trigger that starts now fires it at once");

        clock.Advance(TimeSpan.FromSeconds(30));

        // The same instance back again, exactly as a caller who read it, changed something and returned
        // it would hand it over - and as the XML processor's own reschedule does.
        await scheduler.RescheduleJob(trigger.Key, trigger);

        ITrigger stored = (await scheduler.GetTrigger(trigger.Key))!;
        stored.StartTimeUtc.Should().Be(clock.GetUtcNow(),
            "a repeating simple trigger that has never fired and whose start is in the past starts from now");
        stored.NextFireTimeUtc.Should().Be(stored.StartTimeUtc,
            "the fire time computed before the start moved is behind it, and a trigger stored that way "
            + "fires there and then immediately again at its own start");
    }

    [Test]
    public async Task AFireTimeAheadOfTheStartTimeIsKept()
    {
        FakeTimeProvider clock = new FakeTimeProvider(start);
        await using StandaloneSchedulerFactory factory = CreateFactory(clock, "reschedule-keeps");
        IScheduler scheduler = await factory.GetScheduler();

        ITrigger trigger = RepeatingTrigger(clock);
        await scheduler.ScheduleJob(Job(), trigger);

        clock.Advance(TimeSpan.FromSeconds(30));
        DateTimeOffset forced = start.AddMinutes(5);
        ((IOperableTrigger) trigger).NextFireTimeUtc = forced;

        await scheduler.RescheduleJob(trigger.Key, trigger);

        ITrigger stored = (await scheduler.GetTrigger(trigger.Key))!;
        stored.NextFireTimeUtc.Should().Be(forced,
            "a next fire time the caller set is the caller's decision, and rescheduling is how it is "
            + "made - only one that contradicts the trigger's own start time is replaced");
    }

    private static StandaloneSchedulerFactory CreateFactory(TimeProvider clock, string name)
    {
        return QuartzSchedulerBuilder.Create()
            .UseInMemoryStore()
            .UseTimeProvider(clock)
            .ConfigureScheduler(o => o.InstanceName = name)
            .Build();
    }

    private static IJobDetail Job() => JobBuilder.Create<NoOpJob>().WithIdentity("job1").Build();

    private static ITrigger RepeatingTrigger(TimeProvider clock)
    {
        return TriggerBuilder.Create(clock)
            .WithIdentity("trigger1")
            .ForJob("job1")
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever())
            .Build();
    }
}
