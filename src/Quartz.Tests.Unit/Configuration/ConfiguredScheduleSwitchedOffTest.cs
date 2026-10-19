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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A configured schedule of <c>-</c> switches a trigger off (#4137): through <c>AddConfiguredTrigger</c>,
/// which a declared trigger's <c>ConfigurationKey</c> is generated into, and through a schedule string in the
/// <c>Quartz:Schedule</c> section.
/// </summary>
/// <remarks>
/// The registrations are applied as the scheduler is created, so no scheduler here is started. A SQLite file
/// stands for the persistent store that already holds a copy from an earlier start.
/// </remarks>
public sealed class ConfiguredScheduleSwitchedOffTest
{
    /// <summary>Log event <c>ConfigurationLog.TriggerSwitchedOffByConfiguration</c>.</summary>
    private const int TriggerSwitchedOff = 4032;

    private const string CronKey = "Jobs:Report:Cron";
    private const string Declared = "0 0 6 * * ?";

    private static readonly JobKey reportJob = new("report", "reports");
    private static readonly TriggerKey reportTrigger = new("report-morning", "reports");

    private SqliteTestDatabase database = null!;
    private RecordingLoggerProvider logs = null!;

    [SetUp]
    public void SetUp()
    {
        database = new SqliteTestDatabase("switched-off");
        logs = new RecordingLoggerProvider();
    }

    [TearDown]
    public void TearDown()
    {
        logs.Dispose();
        database.Dispose();
    }

    [Test]
    public async Task ADashSwitchesTheTriggerOffAndSaysSo()
    {
        await Apply(persistent: false, Configured("-"), async scheduler =>
        {
            (await scheduler.GetTrigger(reportTrigger)).Should().BeNull("the configured value switched the trigger off");
            (await scheduler.GetJobDetail(reportJob)).Should().NotBeNull("the job is durable, and stays without its trigger");
        }, durable: true);

        LogEntry entry = logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerSwitchedOff, "one switched-off trigger is one line").Which;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Be($"Trigger '{reportTrigger}' is switched off: '{CronKey}' is '-', so it is not scheduled. A copy already stored was unscheduled: False",
            "an empty store held no copy, and the line names the key to look in");
    }

    [Test]
    public async Task ADashUnschedulesTheCopyAPersistentStoreAlreadyHolds()
    {
        await Apply(persistent: true, Configured("0 0 4 * * ?"), async scheduler =>
            ((ICronTrigger) (await scheduler.GetTrigger(reportTrigger))!).CronExpressionString.Should().Be("0 0 4 * * ?",
                "the premise: an earlier start stored the trigger on its configured schedule"), durable: true);

        await Apply(persistent: true, Configured(" - "), async scheduler =>
            (await scheduler.GetTrigger(reportTrigger)).Should().BeNull(
                "switching the trigger off in configuration stops the store firing the copy it kept"), durable: true);

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerSwitchedOff)
            .Which.Message.Should().EndWith("A copy already stored was unscheduled: True");
    }

    /// <summary>
    /// A non-durable job with nothing left to fire it is not stored, rather than failing the start, and the
    /// copy a store kept goes with its last trigger, as a non-durable job always does.
    /// </summary>
    [Test]
    public async Task ANonDurableJobWhoseOnlyTriggerIsSwitchedOffIsNotStoredAndItsStoredCopyGoes()
    {
        await Apply(persistent: true, Configured(Declared), async scheduler =>
            (await scheduler.GetJobDetail(reportJob)).Should().NotBeNull("the premise: an earlier start stored the job"), durable: false);

        await Apply(persistent: true, Configured("-"), async scheduler =>
        {
            (await scheduler.GetTrigger(reportTrigger)).Should().BeNull();
            (await scheduler.GetJobDetail(reportJob)).Should().BeNull(
                "a non-durable job is deleted with its last trigger, and the registration does not store it again");
        }, durable: false);
    }

    [Test]
    public async Task ANonDurableJobWhoseOnlyTriggerIsSwitchedOffStartsOnAnEmptyStore()
    {
        await Apply(persistent: false, Configured("-"), async scheduler =>
            (await scheduler.GetJobDetail(reportJob)).Should().BeNull(
                "with no trigger to fire it, the job is left out instead of refused as a non-durable job without triggers"), durable: false);
    }

    [Test]
    public async Task TheCallbackIsHandedTheConfiguredValueAndNothingWhenTheKeyIsUnsetOrSwitchedOff()
    {
        List<string?> handed = [];
        await Apply(persistent: false, q =>
        {
            q.AddConfiguredTrigger<IJob>("Jobs:Set", Read, (_, configured, trigger) => Spell(trigger, handed, configured, "set"));
            q.AddConfiguredTrigger<IJob>("Jobs:Unset", Read, (_, configured, trigger) => Spell(trigger, handed, configured, "unset"));
            q.AddConfiguredTrigger<IJob>("Jobs:Off", Read, (_, configured, trigger) => Spell(trigger, handed, configured, "off"));
        }, scheduler => Task.CompletedTask, durable: true, configuration: new Dictionary<string, string?>
        {
            ["Jobs:Set"] = "0 0 4 * * ?",
            ["Jobs:Off"] = "-",
        });

        handed.Should().Equal(["0 0 4 * * ?", null, null],
            "a value is the callback's to spell; an unset key and a switched-off one leave it the declared schedule");
    }

    [TestCase("")]
    [TestCase("--")]
    [TestCase("every morning")]
    public async Task AnyOtherValueIsHandedOnAndAValueTheParserRefusesStillStopsTheStart(string configured)
    {
        Func<Task> act = () => Apply(persistent: false, Configured(configured), _ => Task.CompletedTask, durable: true);

        Cause<FormatException>((await act.Should().ThrowAsync<Exception>()).Which)
            .Should().NotBeNull("only '-' switches a trigger off; anything else is a schedule, and this one does not parse");
    }

    [TestCase("Cron:Expression")]
    [TestCase("Simple:Interval")]
    [TestCase("Recurrence:Rule")]
    public async Task ADashInAJsonScheduleStringSwitchesTheTriggerOff(string scheduleString)
    {
        await ApplyJson(persistent: false, scheduleString, "-", async scheduler =>
        {
            (await scheduler.GetTrigger(new TriggerKey("nightly", "files"))).Should().BeNull();
            (await scheduler.GetJobDetail(new JobKey("sweep", "files"))).Should().NotBeNull("the job is durable");
        });

        logs.Entries.Should().ContainSingle(x => x.EventId.Id == TriggerSwitchedOff)
            .Which.Message.Should().Contain($"'Quartz:Schedule:Triggers:0:{scheduleString}' is '-'",
                "the line names the setting by its path, so an operator can find it");
    }

    [Test]
    public async Task ADashInAJsonScheduleUnschedulesTheCopyAPersistentStoreAlreadyHolds()
    {
        await ApplyJson(persistent: true, "Cron:Expression", "0 0 2 * * ?", async scheduler =>
            (await scheduler.GetTrigger(new TriggerKey("nightly", "files"))).Should().NotBeNull("the premise: an earlier start stored it"));

        await ApplyJson(persistent: true, "Cron:Expression", "-", async scheduler =>
            (await scheduler.GetTrigger(new TriggerKey("nightly", "files"))).Should().BeNull());
    }

    /// <summary>
    /// A switched-off trigger is read no further than its identity, so a value elsewhere in it that would
    /// refuse the file does not; one that names no job is still refused, since it has no identity to read.
    /// </summary>
    [Test]
    public async Task AJsonTriggerSwitchedOffIsNotReadPastItsIdentity()
    {
        await ApplyJson(persistent: false, "Cron:Expression", "-", async scheduler =>
            (await scheduler.GetTrigger(new TriggerKey("nightly", "files"))).Should().BeNull(),
            extra: new Dictionary<string, string?> { ["Quartz:Schedule:Triggers:0:Priority"] = "high" });

        Func<Task> nameless = () => ApplyJson(persistent: false, "Cron:Expression", "-", _ => Task.CompletedTask,
            extra: new Dictionary<string, string?> { ["Quartz:Schedule:Triggers:0:JobName"] = "" });

        Cause<SchedulerConfigException>((await nameless.Should().ThrowAsync<Exception>()).Which)!
            .Message.Should().Contain("missing required 'JobName'");
    }

    [Test]
    public async Task AnEmptyJsonCronExpressionIsStillRefused()
    {
        Func<Task> act = () => ApplyJson(persistent: false, "Cron:Expression", "", _ => Task.CompletedTask);

        Cause<SchedulerConfigException>((await act.Should().ThrowAsync<Exception>()).Which)!
            .Message.Should().Contain("missing required 'Expression'", "an empty schedule string is a mistake, not a switch");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// The registration the generator writes for <c>[CronTrigger("0 0 6 * * ?", ConfigurationKey = "Jobs:Report:Cron")]</c>,
    /// with the value configured.
    /// </summary>
    private static (Action<IQuartzBuilder> Configure, Dictionary<string, string?> Configuration) Configured(string value)
    {
        return (
            q => q.AddConfiguredTrigger<IJob>(CronKey, Read, (_, configured, trigger) => trigger
                .WithIdentity(reportTrigger)
                .ForJob(reportJob)
                .WithCronSchedule(configured ?? Declared)),
            new Dictionary<string, string?> { [CronKey] = value });
    }

    /// <summary>
    /// The first exception of type <typeparamref name="T" /> in the chain, whatever the container wrapped it in.
    /// </summary>
    private static T? Cause<T>(Exception? exception) where T : Exception
    {
        while (exception is not null and not T)
        {
            exception = exception.InnerException;
        }

        exception.Should().BeAssignableTo<T>("the refusal itself is what reaches the host, inside whatever carried it");
        return (T?) exception;
    }

    private static string? Read(IServiceProvider services, string key)
    {
        return services.GetService<IConfiguration>()?[key];
    }

    private static void Spell(ITriggerConfigurator<IJob> trigger, List<string?> handed, string? configured, string name)
    {
        handed.Add(configured);
        trigger.WithIdentity(name, "spelled").ForJob(reportJob).WithCronSchedule(configured ?? Declared);
    }

    private Task Apply(
        bool persistent,
        (Action<IQuartzBuilder> Configure, Dictionary<string, string?> Configuration) registration,
        Func<IScheduler, Task> inspect,
        bool durable)
    {
        return Apply(persistent, registration.Configure, inspect, durable, registration.Configuration);
    }

    private async Task Apply(
        bool persistent,
        Action<IQuartzBuilder> configure,
        Func<IScheduler, Task> inspect,
        bool durable,
        Dictionary<string, string?> configuration)
    {
        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddQuartz(q =>
        {
            Configure(q, persistent);
            q.AddJob<NoOpJob>(job => job.WithIdentity(reportJob).StoreDurably(durable));
            configure(q);
        });

        await Inspect(services, inspect);
    }

    private async Task ApplyJson(
        bool persistent,
        string scheduleString,
        string value,
        Func<IScheduler, Task> inspect,
        Dictionary<string, string?>? extra = null)
    {
        Dictionary<string, string?> settings = new()
        {
            ["Quartz:Schedule:Jobs:0:Name"] = "sweep",
            ["Quartz:Schedule:Jobs:0:Group"] = "files",
            ["Quartz:Schedule:Jobs:0:JobType"] = typeof(NoOpJob).AssemblyQualifiedName,
            ["Quartz:Schedule:Jobs:0:Durable"] = "true",
            ["Quartz:Schedule:Triggers:0:Name"] = "nightly",
            ["Quartz:Schedule:Triggers:0:Group"] = "files",
            ["Quartz:Schedule:Triggers:0:JobName"] = "sweep",
            ["Quartz:Schedule:Triggers:0:JobGroup"] = "files",
            [$"Quartz:Schedule:Triggers:0:{scheduleString}"] = value,
        };

        foreach (KeyValuePair<string, string?> setting in extra ?? [])
        {
            settings[setting.Key] = setting.Value;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddQuartz(configuration.GetSection("Quartz"), q => Configure(q, persistent));

        await Inspect(services, inspect);
    }

    private void Configure(IQuartzBuilder q, bool persistent)
    {
        q.ConfigureScheduler(options =>
        {
            options.InstanceName = "switched-off";
            options.InstanceId = "one";
        });

        if (persistent)
        {
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        }
    }

    private static async Task Inspect(ServiceCollection services, Func<IScheduler, Task> inspect)
    {
        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            await inspect(scheduler);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
