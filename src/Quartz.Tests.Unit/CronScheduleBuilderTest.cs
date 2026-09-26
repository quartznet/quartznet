using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit;

public class CronScheduleBuilderTest
{
    [Test]
    public void TakesACronExpressionAsAString()
    {
        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *"))
            .Build();

        trigger.CronExpressionString.Should().Be("0 20 10 ? * *");
    }

    [Test]
    public void TakesACronExpressionBuiltElsewhere()
    {
        CronExpression expression = CronExpressionBuilder.Create()
            .WithSecond(0)
            .WithMinute(0)
            .WithHour(10)
            .WithDaysOfWeek(DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Friday)
            .Build();

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create(expression))
            .Build();

        trigger.CronExpressionString.Should().Be("0 0 10 ? * MON,THU,FRI");
    }

    [Test]
    public void WithCronScheduleTakesACronExpressionDirectly()
    {
        CronExpression expression = new CronExpression("0 0 10 ? * MON", TimeZoneInfo.Utc);

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithCronSchedule(expression)
            .Build();

        trigger.CronExpressionString.Should().Be("0 0 10 ? * MON");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Test]
    public void WithCronScheduleTakesACronExpressionBuilderDirectly()
    {
        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithCronSchedule(
                CronExpressionBuilder.Create()
                    .WithSecond(0)
                    .WithMinuteIncrements(0, 15)
                    .WithHourRange(8, 17)
                    .OnWeekdays(),
                x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.CronExpressionString.Should().Be("0 0/15 8-17 ? * MON-FRI");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    /// <summary>
    /// The inline shape, in every way a delegate can be written. Each of these compiling at all is half
    /// the test: the other overloads' first parameters are a string, a <see cref="CronExpression" />, a
    /// <see cref="CronExpressionBuilder" /> and a <see cref="CronScheduleBuilder" />, none of which a
    /// lambda or a method group converts to, so the new one is the only candidate.
    /// </summary>
    [Test]
    public void WithCronScheduleAssemblesTheExpressionInline()
    {
        ICronTrigger expressionBodied = (ICronTrigger) TriggerBuilder.Create()
            .WithCronSchedule(cron => cron.AtTime(new TimeOnly(3, 0)).OnWeekdays())
            .Build();

        ICronTrigger blockBodied = (ICronTrigger) TriggerBuilder.Create()
            .WithCronSchedule(cron =>
            {
                cron.Every(TimeSpan.FromMinutes(10));
                cron.WithHourRange(8, 17);
            })
            .Build();

        ICronTrigger methodGroup = (ICronTrigger) TriggerBuilder.Create()
            .WithCronSchedule(EveryQuarterHour)
            .Build();

        ICronTrigger withConfigure = (ICronTrigger) TriggerBuilder.Create()
            .WithCronSchedule(cron => cron.Every(TimeSpan.FromHours(6)), x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        ICronTrigger named = (ICronTrigger) TriggerBuilder.Create()
            .WithCronSchedule(expression: cron => cron.OnLastDayOfMonth(), configure: x => x.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .Build();

        expressionBodied.CronExpressionString.Should().Be("0 0 3 ? * MON-FRI");
        blockBodied.CronExpressionString.Should().Be("0 0/10 8-17 ? * *");
        methodGroup.CronExpressionString.Should().Be("0 0/15 * ? * *");
        withConfigure.CronExpressionString.Should().Be("0 0 0/6 ? * *");
        withConfigure.TimeZone.Should().Be(TimeZoneInfo.Utc, "the configure callback applies to the schedule, as on the other overloads");
        named.CronExpressionString.Should().Be("* * * L * ?");
        named.MisfireInstruction.Should().Be(CronTriggerMisfireInstruction.DoNothing);
    }

    /// <summary>
    /// The container's configurator reads the same as the builder, because both are the receiver the
    /// extension is generic in.
    /// </summary>
    [Test]
    public async Task WithCronScheduleAssemblesTheExpressionInlineThroughTheContainer()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = $"inline-cron-{Guid.NewGuid():N}");
            q.AddJob<NoOpJob>(job => job.WithIdentity("inline").StoreDurably());
            q.AddTrigger<NoOpJob>(trigger => trigger
                .WithIdentity("inline")
                .ForJob("inline")
                .WithCronSchedule(cron => cron.Every(TimeSpan.FromMinutes(10)).OnWeekdays()));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        ICronTrigger trigger = (ICronTrigger) (await scheduler.GetTrigger(new TriggerKey("inline")))!;
        trigger.CronExpressionString.Should().Be("0 0/10 * ? * MON-FRI");

        await scheduler.Shutdown();
    }

    [Test]
    public void WithCronScheduleRefusesAnInlineExpressionTheBuilderRefuses()
    {
        Action contradiction = () => TriggerBuilder.Create().WithCronSchedule(cron => cron.Every(TimeSpan.FromMinutes(10)).AtTime(new TimeOnly(3, 0)));
        Action uneven = () => TriggerBuilder.Create().WithCronSchedule(cron => cron.Every(TimeSpan.FromMinutes(7)));
        Action missing = () => TriggerBuilder.Create().WithCronSchedule((Action<CronExpressionBuilder>) null!);

        contradiction.Should().Throw<InvalidOperationException>("the expression is assembled where the call is written, so its refusal is too");
        uneven.Should().Throw<ArgumentOutOfRangeException>();
        missing.Should().Throw<ArgumentNullException>().WithParameterName("expression");
    }

    private static void EveryQuarterHour(CronExpressionBuilder cron) => cron.Every(TimeSpan.FromMinutes(15));

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [Test]
    public void RejectsAnExpressionItCannotParse()
    {
        Action act = () => CronScheduleBuilder.Create("not a cron expression");

        act.Should().Throw<FormatException>();
    }

    [Test]
    public void ParsesTheExpressionOnce()
    {
        const string Expression = "0 20 10 ? * *";

        // Both measurements are of a warm path: the first call through either one pays for JIT and for
        // whatever the parser caches, and neither is what this is counting.
        CronScheduleBuilder.Create(Expression);
        _ = new CronExpression(Expression);

        long oneParse = Allocated(static () => _ = new CronExpression(Expression));
        long create = Allocated(static () => CronScheduleBuilder.Create(Expression));

        create.Should().BeLessThan(oneParse + oneParse / 2,
            "Create validated the expression by constructing one it threw away and then constructed the one it keeps, so every WithCronSchedule(string) parsed its expression twice - and two parses cannot fit under one and a half");
    }

    [Test]
    public void CarriesTheTimeZoneOntoTheTrigger()
    {
        TimeZoneInfo timeZone = TestTimeZones.CentralEuropean;

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *").InTimeZone(timeZone))
            .Build();

        trigger.TimeZone.Should().Be(timeZone);
    }

    [Test]
    public void FallsBackToTheLocalTimeZoneWhenGivenNull()
    {
        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *").InTimeZone(null))
            .Build();

        trigger.TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    [Test]
    public void ChangingTheTimeZoneDoesNotRetimeAlreadyBuiltTriggers()
    {
        CronScheduleBuilder schedule = CronScheduleBuilder.Create("0 20 10 ? * *");

        ICronTrigger first = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("first")
            .WithSchedule(schedule)
            .Build();

        schedule.InTimeZone(TestTimeZones.CentralEuropean);

        ICronTrigger second = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("second")
            .WithSchedule(schedule)
            .Build();

        first.TimeZone.Should().Be(TimeZoneInfo.Local,
            "the builder used to hand every trigger the same mutable CronExpression, so a later InTimeZone silently retimed triggers that were already built");
        second.TimeZone.Should().Be(TestTimeZones.CentralEuropean);
    }

    [Test]
    public void ChangingTheTimeZoneDoesNotRetimeTheCallersExpression()
    {
        CronExpression expression = new CronExpression("0 20 10 ? * *");

        TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create(expression).InTimeZone(TestTimeZones.CentralEuropean))
            .Build();

        expression.TimeZone.Should().Be(TimeZoneInfo.Local,
            "InTimeZone must reshape the builder's copy, not write through the instance the caller still holds");
    }

    [Test]
    public void DefaultsToTheSmartMisfirePolicy()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *"))
            .Build();

        trigger.MisfireInstructionCode.Should().Be(MisfireInstruction.SmartPolicy);
    }

    [TestCase(CronTriggerMisfireInstruction.IgnoreMisfires, MisfireInstruction.IgnoreMisfirePolicy)]
    [TestCase(CronTriggerMisfireInstruction.DoNothing, MisfireInstruction.CronTrigger.DoNothing)]
    [TestCase(CronTriggerMisfireInstruction.FireAndProceed, MisfireInstruction.CronTrigger.FireOnceNow)]
    public void StoresTheMisfireInstructionAsItsConstant(CronTriggerMisfireInstruction instruction, int stored)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithCronSchedule("0 20 10 ? * *", x => x.WithMisfireInstruction(instruction))
            .Build();

        trigger.MisfireInstructionCode.Should().Be(stored);
    }

    private static long Allocated(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
