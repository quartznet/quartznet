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

using FakeItEasy;

using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// The scheduler-wide default misfire instruction: what it maps to in each trigger family, which triggers
/// take it — an unstated instruction, never an explicit smart policy — and where it is resolved (#4070).
/// </summary>
[TestFixture]
public class DefaultMisfireHandlingTest
{
    /// <summary>The way a caller hands a trigger to the scheduler.</summary>
    public enum SchedulePath
    {
        ScheduleJobWithTrigger,
        ScheduleJobsBatch,
        ScheduleTriggerAlone,
        ScheduleTriggerOnConflict,
        RescheduleJob
    }

    private static readonly object[] mappings =
    [
        new object[] { "simple", DefaultMisfireHandling.SmartPolicy, (int) SimpleTriggerMisfireInstruction.SmartPolicy },
        new object[] { "simple", DefaultMisfireHandling.IgnoreMisfires, (int) SimpleTriggerMisfireInstruction.IgnoreMisfires },
        new object[] { "simple", DefaultMisfireHandling.FireAndProceed, (int) SimpleTriggerMisfireInstruction.FireNow },
        new object[] { "simple", DefaultMisfireHandling.DoNothing, (int) SimpleTriggerMisfireInstruction.NextWithRemainingCount },
        new object[] { "cron", DefaultMisfireHandling.IgnoreMisfires, (int) CronTriggerMisfireInstruction.IgnoreMisfires },
        new object[] { "cron", DefaultMisfireHandling.FireAndProceed, (int) CronTriggerMisfireInstruction.FireAndProceed },
        new object[] { "cron", DefaultMisfireHandling.DoNothing, (int) CronTriggerMisfireInstruction.DoNothing },
        new object[] { "calendar", DefaultMisfireHandling.IgnoreMisfires, (int) CalendarIntervalTriggerMisfireInstruction.IgnoreMisfires },
        new object[] { "calendar", DefaultMisfireHandling.FireAndProceed, (int) CalendarIntervalTriggerMisfireInstruction.FireAndProceed },
        new object[] { "calendar", DefaultMisfireHandling.DoNothing, (int) CalendarIntervalTriggerMisfireInstruction.DoNothing },
        new object[] { "daily", DefaultMisfireHandling.IgnoreMisfires, (int) DailyTimeIntervalTriggerMisfireInstruction.IgnoreMisfires },
        new object[] { "daily", DefaultMisfireHandling.FireAndProceed, (int) DailyTimeIntervalTriggerMisfireInstruction.FireAndProceed },
        new object[] { "daily", DefaultMisfireHandling.DoNothing, (int) DailyTimeIntervalTriggerMisfireInstruction.DoNothing },
        new object[] { "recurrence", DefaultMisfireHandling.IgnoreMisfires, (int) RecurrenceTriggerMisfireInstruction.IgnoreMisfires },
        new object[] { "recurrence", DefaultMisfireHandling.FireAndProceed, (int) RecurrenceTriggerMisfireInstruction.FireAndProceed },
        new object[] { "recurrence", DefaultMisfireHandling.DoNothing, (int) RecurrenceTriggerMisfireInstruction.DoNothing },
    ];

    [TestCaseSource(nameof(mappings))]
    public void TheDefaultMapsToEachFamilysOwnInstruction(string family, DefaultMisfireHandling handling, int expected)
    {
        ITrigger trigger = Build(family, stated: null);

        MisfireDefaults.CodeFor(trigger, handling).Should().Be(expected,
            "instruction codes are per family, so the default is mapped to the family's own");

        MisfireDefaults.Apply((IMutableTrigger) trigger, handling);
        trigger.MisfireInstructionCode.Should().Be(expected);
    }

    [Test]
    public void ATriggerInNoFamilyHasNoMappingAndIsLeftAlone()
    {
        ITrigger stranger = A.Fake<ITrigger>();

        MisfireDefaults.CodeFor(stranger, DefaultMisfireHandling.DoNothing).Should().BeNull(
            "Quartz cannot know what a trigger type of somebody else's numbers its instructions as");
        MisfireDefaults.CodeFor(stranger, DefaultMisfireHandling.IgnoreMisfires).Should().BeNull();
        MisfireDefaults.CodeFor(stranger, DefaultMisfireHandling.SmartPolicy).Should().Be(0);

        IMutableTrigger mutable = A.Fake<IMutableTrigger>();
        MisfireDefaults.Apply(mutable, DefaultMisfireHandling.DoNothing);
        A.CallToSet(() => mutable.MisfireInstructionCode).MustNotHaveHappened();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Stated or not
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void ABuilderNeverGivenAnInstructionLeavesItUnstated()
    {
        TriggerBase unstated = (TriggerBase) Build("cron", stated: null);
        TriggerBase smart = (TriggerBase) Build("cron", stated: 0);

        unstated.MisfireInstructionStated.Should().BeFalse();
        unstated.MisfireInstructionCode.Should().Be(0);
        smart.MisfireInstructionStated.Should().BeTrue("an explicit smart policy is stated, though its code reads the same as an unset one");
        smart.MisfireInstructionCode.Should().Be(0);
    }

    [Test]
    public void ARebuildKeepsWhetherTheInstructionWasStated()
    {
        foreach (string family in new[] { "simple", "cron", "calendar", "daily", "recurrence" })
        {
            ((TriggerBase) Build(family, stated: null).GetTriggerBuilder().Build()).MisfireInstructionStated.Should().BeFalse(
                $"a rebuilt {family} trigger leaves unstated what the original left unstated");
            ((TriggerBase) Build(family, stated: 0).GetTriggerBuilder().Build()).MisfireInstructionStated.Should().BeTrue(
                $"a rebuilt {family} trigger keeps a smart policy somebody stated");
        }

        ((TriggerBase) Build("cron", stated: null).Clone()).MisfireInstructionStated.Should().BeFalse("a clone is the same trigger");
    }

    [Test]
    public void AnAssignedOrDeserializedInstructionIsStated()
    {
        SimpleTriggerImpl assigned = new() { MisfireInstructionCode = 0 };
        assigned.MisfireInstructionStated.Should().BeTrue("any assignment states the instruction, zero included");

        SystemTextJsonObjectSerializer serializer = new();
        byte[] payload = serializer.Serialize((IOperableTrigger) Build("cron", stated: null));
        ((TriggerBase) serializer.Deserialize<IOperableTrigger>(payload)!).MisfireInstructionStated.Should().BeTrue(
            "a deserialized trigger's instruction was decided when it was written");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Where the scheduler resolves it
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCase(SchedulePath.ScheduleJobWithTrigger)]
    [TestCase(SchedulePath.ScheduleJobsBatch)]
    [TestCase(SchedulePath.ScheduleTriggerAlone)]
    [TestCase(SchedulePath.ScheduleTriggerOnConflict)]
    [TestCase(SchedulePath.RescheduleJob)]
    public async Task AnUnstatedInstructionTakesTheDefaultWhenTheTriggerIsStored(SchedulePath path)
    {
        IScheduler scheduler = await BuildScheduler(DefaultMisfireHandling.DoNothing);
        try
        {
            ITrigger stored = await StoreThrough(scheduler, path, Build("cron", stated: null, key: "unstated"));

            stored.MisfireInstructionCode.Should().Be((int) CronTriggerMisfireInstruction.DoNothing,
                $"{path} resolves an unstated instruction to the scheduler's default, so the store holds an explicit one");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [TestCase(SchedulePath.ScheduleJobWithTrigger)]
    [TestCase(SchedulePath.ScheduleJobsBatch)]
    [TestCase(SchedulePath.ScheduleTriggerAlone)]
    [TestCase(SchedulePath.ScheduleTriggerOnConflict)]
    [TestCase(SchedulePath.RescheduleJob)]
    public async Task AnExplicitSmartPolicyKeepsTheSmartPolicy(SchedulePath path)
    {
        IScheduler scheduler = await BuildScheduler(DefaultMisfireHandling.DoNothing);
        try
        {
            ITrigger stored = await StoreThrough(scheduler, path, Build("cron", stated: 0, key: "smart"));

            stored.MisfireInstructionCode.Should().Be((int) CronTriggerMisfireInstruction.SmartPolicy,
                "an explicit smart policy is stated, and the default leaves a stated instruction alone");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task TheSmartDefaultChangesNothing()
    {
        IScheduler scheduler = await BuildScheduler(DefaultMisfireHandling.SmartPolicy);
        try
        {
            ITrigger stored = await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build("simple", stated: null, key: "unstated"));

            stored.MisfireInstructionCode.Should().Be(0, "SmartPolicy, the default default, is what every trigger did before");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task TheFlatKeySetsTheDefault()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "DefaultMisfireHandlingTest-flat",
            ["quartz.scheduler.defaultMisfireHandling"] = "fireAndProceed",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };

        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        IScheduler scheduler = await factory.GetScheduler();
        try
        {
            ITrigger stored = await StoreThrough(scheduler, SchedulePath.ScheduleJobWithTrigger, Build("daily", stated: null, key: "unstated"));

            stored.MisfireInstructionCode.Should().Be((int) DailyTimeIntervalTriggerMisfireInstruction.FireAndProceed,
                "the flat key is read by member name, in any case");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task AFlatKeyNamingNoDefaultIsRefused()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "DefaultMisfireHandlingTest-refused",
            ["quartz.scheduler.defaultMisfireHandling"] = "Sometimes"
        };

        using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
        Func<Task> act = async () => await factory.GetScheduler();

        await act.Should().ThrowAsync<SchedulerConfigException>("a misspelled default would leave every unstated trigger on the smart policy unannounced")
            .WithMessage("*Sometimes*quartz.scheduler.defaultMisfireHandling*SmartPolicy*");
    }

    [Test]
    public void AnUndefinedDefaultFailsValidation()
    {
        QuartzSchedulerOptionsValidator validator = new(A.Fake<IOptionsMonitor<ThreadPoolOptions>>());

        ValidateOptionsResult result = validator.Validate(Options.DefaultName, new QuartzSchedulerOptions { DefaultMisfireHandling = (DefaultMisfireHandling) 9 });

        result.Failed.Should().BeTrue("a number cast to the enum maps to nothing, and every unstated trigger would keep the smart policy unannounced");
        result.FailureMessage.Should().Contain("DefaultMisfireHandling");
    }

    private static async Task<IScheduler> BuildScheduler(DefaultMisfireHandling handling)
    {
        return await QuartzSchedulerBuilder.Create(q => q.ConfigureScheduler(options =>
            {
                options.InstanceName = "DefaultMisfireHandlingTest-" + Guid.NewGuid().ToString("N");
                options.DefaultMisfireHandling = handling;
            }))
            .BuildScheduler();
    }

    private static async Task<ITrigger> StoreThrough(IScheduler scheduler, SchedulePath path, ITrigger trigger)
    {
        IJobDetail job = JobBuilder.Create<DefaultMisfireJob>().WithIdentity("job", "g").StoreDurably().Build();

        switch (path)
        {
            case SchedulePath.ScheduleJobWithTrigger:
                await scheduler.ScheduleJob(job, trigger);
                break;
            case SchedulePath.ScheduleJobsBatch:
                await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = [trigger] });
                break;
            case SchedulePath.ScheduleTriggerAlone:
                await scheduler.AddJob(job);
                await scheduler.ScheduleJob(trigger);
                break;
            case SchedulePath.ScheduleTriggerOnConflict:
                await scheduler.AddJob(job);
                await scheduler.ScheduleTrigger(trigger, TriggerConflict.Replace);
                break;
            case SchedulePath.RescheduleJob:
                ITrigger placeholder = TriggerBuilder.Create()
                    .WithIdentity(trigger.Key)
                    .ForJob(job)
                    .WithCronSchedule("0 0 1 * * ?", x => x.WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
                    .Build();
                await scheduler.ScheduleJob(job, placeholder);
                await scheduler.RescheduleJob(trigger.Key, trigger);
                break;
        }

        return (await scheduler.GetTrigger(trigger.Key))!;
    }

    private static ITrigger Build(string family, int? stated, string key = "t")
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create().WithIdentity(key, "g").ForJob("job", "g");

        return family switch
        {
            "simple" => builder.WithSimpleSchedule(x =>
            {
                x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever();
                if (stated is { } code)
                {
                    x.WithMisfireInstruction((SimpleTriggerMisfireInstruction) code);
                }
            }).Build(),
            "cron" => builder.WithCronSchedule("0 0 3 * * ?", x =>
            {
                if (stated is { } code)
                {
                    x.WithMisfireInstruction((CronTriggerMisfireInstruction) code);
                }
            }).Build(),
            "calendar" => builder.WithCalendarIntervalSchedule(x =>
            {
                x.WithInterval(1, IntervalUnit.Day);
                if (stated is { } code)
                {
                    x.WithMisfireInstruction((CalendarIntervalTriggerMisfireInstruction) code);
                }
            }).Build(),
            "daily" => builder.WithDailyTimeIntervalSchedule(x =>
            {
                x.WithInterval(1, IntervalUnit.Hour);
                if (stated is { } code)
                {
                    x.WithMisfireInstruction((DailyTimeIntervalTriggerMisfireInstruction) code);
                }
            }).Build(),
            "recurrence" => builder.WithRecurrenceSchedule("FREQ=DAILY", x =>
            {
                if (stated is { } code)
                {
                    x.WithMisfireInstruction((RecurrenceTriggerMisfireInstruction) code);
                }
            }).Build(),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "not a trigger family")
        };
    }

    /// <summary>Never executed: the triggers under test are stored, not fired.</summary>
    public sealed class DefaultMisfireJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
