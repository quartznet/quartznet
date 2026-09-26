#nullable enable

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit;

/// <summary>
/// An execution group derived from the data a trigger carries: <c>{key}</c> placeholders resolved when
/// the trigger is built, from <c>[ExecutionGroup]</c> on the job type or from the builder.
/// </summary>
/// <remarks>
/// The trigger stores the resolved name, so everything past <c>Build()</c> — the store, the wire, the
/// limits — sees an ordinary group. What is asserted here is the resolution and the ways it refuses.
/// </remarks>
public sealed class ExecutionGroupTemplateTest
{
    [Test]
    public void APlaceholderIsResolvedFromTheTriggersJobData()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithExecutionGroup("tenant:{TenantId}")
            .UsingJobData("TenantId", "acme")
            .Build();

        trigger.ExecutionGroup.Should().Be("tenant:acme",
            "the job data is set after the group here, and resolution waits for Build() the way H does");
    }

    [Test]
    public void AValueIsSpelledTheSameOnEveryCulture()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithExecutionGroup("shard-{Shard}/{Weight}")
            .UsingJobData("Shard", 42)
            .UsingJobData("Weight", 1.5m)
            .Build();

        trigger.ExecutionGroup.Should().Be("shard-42/1.5", "a value is formatted invariantly, so every node agrees on the group");
    }

    [Test]
    public void AValueThatIsNotFormattableIsSpelledByItsToString()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithExecutionGroup("premium-{Premium}")
            .UsingJobData("Premium", true)
            .Build();

        trigger.ExecutionGroup.Should().Be("premium-True");
    }

    [Test]
    public void DoubledBracesAreLiteralBraces()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithExecutionGroup("{{literal}}-{Id}")
            .UsingJobData("Id", "7")
            .Build();

        trigger.ExecutionGroup.Should().Be("{literal}-7");
    }

    [Test]
    public void AMissingKeyFailsTheBuildNamingIt()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create().WithExecutionGroup("tenant:{TenantId}");

        Action build = () => builder.Build();

        build.Should().Throw<FormatException>().WithMessage("*'TenantId'*trigger's job data*",
            "a group quietly left unresolved would put every tenant in one literal 'tenant:{TenantId}' bucket");
    }

    [Test]
    public void ANullValueFailsTheBuild()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithExecutionGroup("tenant:{TenantId}")
            .UsingJobData("TenantId", null);

        Action build = () => builder.Build();

        build.Should().Throw<FormatException>().WithMessage("*'TenantId'*null*");
    }

    [TestCase("{Tenant", "*never closes*")]
    [TestCase("tenant}", "*closes nothing*")]
    [TestCase("tenant:{}", "*empty placeholder*")]
    [TestCase("{a{b}", "*inside another*")]
    public void AMalformedTemplateIsRefusedWhereItIsWritten(string template, string message)
    {
        Action set = () => TriggerBuilder.Create().WithExecutionGroup(template);

        set.Should().Throw<ArgumentException>().WithMessage(message,
            "a template that cannot be read is the caller's mistake, and the call site is where to say so");
    }

    [Test]
    public void ATemplateThatResolvesToAReservedNameIsRefused()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithExecutionGroup("{Group}")
            .UsingJobData("Group", "*");

        Action build = () => builder.Build();

        build.Should().Throw<FormatException>().WithMessage("*reserved*",
            "'*' is the catch-all's key, and a trigger in it would be limited by a rule rather than a group");
    }

    [Test]
    public void ATemplateThatResolvesToNothingIsRefused()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create()
            .WithExecutionGroup("{Group}")
            .UsingJobData("Group", "  ");

        Action build = () => builder.Build();

        build.Should().Throw<FormatException>().WithMessage("*empty name*");
    }

    [Test]
    public void ABuiltTriggerRebuildsWithTheSameStoredName()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithExecutionGroup("tenant:{TenantId}")
            .UsingJobData("TenantId", "{curly}")
            .Build();

        trigger.ExecutionGroup.Should().Be("tenant:{curly}", "a value is inserted as it is, braces and all");

        ITrigger rebuilt = trigger.GetTriggerBuilder().Build();

        rebuilt.ExecutionGroup.Should().Be("tenant:{curly}",
            "the stored group is a name, so a rebuilt trigger must not read the braces in it as a placeholder");
    }

    [Test]
    public void TheJobTypesAttributeIsAppliedWhenTheBuilderSetsNoGroup()
    {
        ITrigger trigger = TriggerBuilder.Create<TenantReportJob>()
            .UsingJobData("TenantId", "acme")
            .Build();

        trigger.ExecutionGroup.Should().Be("tenant:acme");
    }

    [Test]
    public void AnExplicitGroupWinsOverTheAttribute()
    {
        ITrigger trigger = TriggerBuilder.Create<TenantReportJob>()
            .WithExecutionGroup("reports")
            .Build();

        trigger.ExecutionGroup.Should().Be("reports");
    }

    [Test]
    public void AnExplicitNullOptsOutOfTheAttribute()
    {
        ITrigger trigger = TriggerBuilder.Create<TenantReportJob>()
            .WithExecutionGroup(null)
            .Build();

        trigger.ExecutionGroup.Should().BeNull("clearing the group is a choice, and the attribute only fills a group nobody chose");
    }

    [Test]
    public void TheAttributeIsInheritedFromABaseJob()
    {
        ITrigger trigger = TriggerBuilder.Create<DerivedTenantReportJob>()
            .UsingJobData("TenantId", "initech")
            .Build();

        trigger.ExecutionGroup.Should().Be("tenant:initech");
    }

    [Test]
    public void ABuilderForIJobReadsNoAttribute()
    {
        ITrigger trigger = TriggerBuilder.Create().Build();

        trigger.ExecutionGroup.Should().BeNull();
    }

    [Test]
    public void AnAttributeWithAPlainNameNeedsNoData()
    {
        ITrigger trigger = TriggerBuilder.Create<PlainGroupJob>().Build();

        trigger.ExecutionGroup.Should().Be("reports");
    }

    [TestCase("")]
    [TestCase("{unclosed")]
    [TestCase("*")]
    public void AnAttributeThatCannotBeAGroupIsRefused(string template)
    {
        Action declare = () => _ = new ExecutionGroupAttribute(template);

        declare.Should().Throw<ArgumentException>();
    }

    [Test]
    public void TheAttributeKeepsItsTemplateTrimmed()
    {
        new ExecutionGroupAttribute("  tenant:{TenantId} ").Template.Should().Be("tenant:{TenantId}");
    }

    [Test]
    public async Task TheOneLinerTakesTheTenantAtTheCallSite()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        TenantInvoice invoice = new("acme", 12m);

        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<TenantInvoiceJob, TenantInvoice>(
            invoice,
            TimeSpan.FromHours(1),
            new OneOffJobOptions { ExecutionGroup = $"tenant:{invoice.TenantId}" });

        ITrigger? stored = await scheduler.GetTrigger(scheduled.TriggerKey);
        stored!.ExecutionGroup.Should().Be("tenant:acme",
            "the options' group wins over the attribute, so a job type declaring a template schedules fine this way");
    }

    [Test]
    public async Task TheOneLinersGroupIsANameAsWritten()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<PlainInvoiceJob, TenantInvoice>(
            new TenantInvoice("acme", 12m),
            TimeSpan.FromHours(1),
            new OneOffJobOptions { ExecutionGroup = "billing:{TenantId}" });

        ITrigger? stored = await scheduler.GetTrigger(scheduled.TriggerKey);
        stored!.ExecutionGroup.Should().Be("billing:{TenantId}",
            "OneOffJobOptions.ExecutionGroup is a name, as it was before templates existed, so its braces are kept");
    }

    [Test]
    public async Task TheOneLinerAppliesAnAttributeWithNoPlaceholders()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<ReportsInvoiceJob, TenantInvoice>(
            new TenantInvoice("acme", 12m),
            TimeSpan.FromHours(1));

        ITrigger? stored = await scheduler.GetTrigger(scheduled.TriggerKey);
        stored!.ExecutionGroup.Should().Be("reports", "a declared name needs no values, so the one-liner applies it");
    }

    [Test]
    public async Task TheOneLinerRefusesAnAttributeItHasNoValuesFor()
    {
        await using ServiceProvider container = BuildContainer();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        Func<Task> schedule = async () => await scheduler.ScheduleJob<TenantInvoiceJob, TenantInvoice>(
            new TenantInvoice("acme", 12m),
            TimeSpan.FromHours(1));

        await schedule.Should().ThrowAsync<FormatException>().WithMessage("*OneOffJobOptions.ExecutionGroup = $\"tenant:{input.TenantId}\"*",
            "storing the firing in no group would put it outside the limit the attribute exists to apply, "
            + "so the call is refused with the spelling that works");
        (await scheduler.GetTriggersOfJob(SchedulerConstants.ScheduledJobKey<TenantInvoiceJob>())).Should().BeEmpty(
            "nothing is stored for a call that was refused");
    }

    [Test]
    public void OnlyABraceThatIsNotDoubledIsAPlaceholder()
    {
        ExecutionGroupTemplate.HasPlaceholders("tenant:{TenantId}").Should().BeTrue();
        ExecutionGroupTemplate.HasPlaceholders("{{literal}}").Should().BeFalse("doubled braces are escapes, not placeholders");
        ExecutionGroupTemplate.HasPlaceholders("{{{Id}}}").Should().BeTrue();
        ExecutionGroupTemplate.HasPlaceholders("reports").Should().BeFalse();
    }

    private static ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = $"template-{Guid.NewGuid():N}"));
        return services.BuildServiceProvider();
    }

    [ExecutionGroup("tenant:{TenantId}")]
    public class TenantReportJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class DerivedTenantReportJob : TenantReportJob;

    [ExecutionGroup("reports")]
    public sealed class PlainGroupJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed record TenantInvoice(string TenantId, decimal Amount);

    [ExecutionGroup("tenant:{TenantId}")]
    public sealed class TenantInvoiceJob : IJob<TenantInvoice>
    {
        public ValueTask Execute(IJobExecutionContext context, TenantInvoice input, CancellationToken cancellationToken = default) => default;
    }

    [ExecutionGroup("reports")]
    public sealed class ReportsInvoiceJob : IJob<TenantInvoice>
    {
        public ValueTask Execute(IJobExecutionContext context, TenantInvoice input, CancellationToken cancellationToken = default) => default;
    }

    public sealed class PlainInvoiceJob : IJob<TenantInvoice>
    {
        public ValueTask Execute(IJobExecutionContext context, TenantInvoice input, CancellationToken cancellationToken = default) => default;
    }
}
