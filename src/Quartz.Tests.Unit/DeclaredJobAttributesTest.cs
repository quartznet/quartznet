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

using System.Reflection;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// What <c>[QuartzJob]</c>, <c>[CronTrigger]</c> and <c>[SimpleTrigger]</c> carry, and what they
/// default to.
/// </summary>
/// <remarks>
/// The attributes are read by the source generator in <c>Quartz.Analyzers</c>, which the tests in
/// <c>Quartz.Analyzers.Tests</c> drive — nothing here runs a generator. What this holds is the half
/// that is a shipped contract whatever reads it: the defaults an unwritten property means, and the
/// usage that lets a job declare several schedules.
/// </remarks>
public class DeclaredJobAttributesTest
{
    [Test]
    public void JobDefaultsToNothingSaid()
    {
        QuartzJobAttribute attribute = new QuartzJobAttribute();

        attribute.Name.Should().BeNull("a job that names nothing is named after its class, and only the generator knows what that class is called");
        attribute.Group.Should().BeNull();
        attribute.Description.Should().BeNull();
        attribute.Durable.Should().BeFalse("durability is forced on for a job with no schedule, which is a decision the generator makes rather than a different default here");
        attribute.RequestRecovery.Should().BeFalse();
        attribute.Scheduler.Should().BeNull("a job naming no scheduler belongs to every scheduler AddDeclaredJobs is called on");
    }

    [Test]
    public void ScheduleDefaultsToTheTriggerDefaults()
    {
        CronTriggerAttribute attribute = new CronTriggerAttribute("0 0 0/6 * * ?");

        attribute.CronExpression.Should().Be("0 0 0/6 * * ?");
        attribute.Priority.Should().Be(TriggerConstants.DefaultPriority, "an unwritten priority has to mean what an unwritten WithPriority means");
        attribute.MisfireInstruction.Should().Be(CronTriggerMisfireInstruction.SmartPolicy);
        attribute.Name.Should().BeNull();
        attribute.Group.Should().BeNull();
        attribute.TimeZone.Should().BeNull();
        attribute.Description.Should().BeNull();
        attribute.ExecutionGroup.Should().BeNull();
        attribute.ConfigurationKey.Should().BeNull("a schedule names no configuration unless it says so, and is then the attribute's alone");
    }

    [Test]
    public void IntervalDefaultsToTheTriggerDefaults()
    {
        SimpleTriggerAttribute attribute = new SimpleTriggerAttribute("00:10:00");

        attribute.Interval.Should().Be(TimeSpan.FromMinutes(10));
        attribute.RepeatCount.Should().Be(SimpleTriggerImpl.RepeatIndefinitely, "an interval job repeats until something stops it unless it says otherwise");
        attribute.Priority.Should().Be(TriggerConstants.DefaultPriority, "an unwritten priority has to mean what an unwritten WithPriority means");
        attribute.MisfireInstruction.Should().Be(SimpleTriggerMisfireInstruction.SmartPolicy);
        attribute.Name.Should().BeNull();
        attribute.Group.Should().BeNull();
        attribute.Description.Should().BeNull();
        attribute.ExecutionGroup.Should().BeNull();
        attribute.ConfigurationKey.Should().BeNull();
    }

    [TestCase("1.00:00:00", 864_000_000_000L)]
    [TestCase("00:00:00.250", 2_500_000L)]
    [TestCase("  00:00:30  ", 300_000_000L)]
    public void IntervalIsReadInvariantly(string interval, long ticks)
    {
        new SimpleTriggerAttribute(interval).Interval.Should().Be(TimeSpan.FromTicks(ticks));
    }

    [Test]
    public void IntervalThatIsNotATimeSpanIsRefused()
    {
        Action act = () => _ = new SimpleTriggerAttribute("ten minutes");

        act.Should().Throw<ArgumentException>()
            .WithMessage("'ten minutes' is not a TimeSpan. Spell the trigger's interval the way TimeSpan does, invariantly: *")
            .Which.ParamName.Should().Be("interval");
    }

    [TestCase("00:00:00")]
    [TestCase("-00:10:00")]
    public void IntervalThatIsNotPositiveIsRefused(string interval)
    {
        Action act = () => _ = new SimpleTriggerAttribute(interval);

        act.Should().Throw<ArgumentOutOfRangeException>("a repeating trigger with no time between firings is one Quartz refuses to schedule")
            .WithMessage($"A trigger's interval has to be longer than zero, and '{interval}' is not.*");
    }

    [Test]
    public void MissingIntervalIsRefused()
    {
        Action act = () => _ = new SimpleTriggerAttribute(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("interval");
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(24)]
    public void RepeatCountOfMinusOneOrMoreIsKept(int repeatCount)
    {
        new SimpleTriggerAttribute("00:10:00") { RepeatCount = repeatCount }.RepeatCount.Should().Be(repeatCount);
    }

    [Test]
    public void RepeatCountBelowMinusOneIsRefused()
    {
        Action act = () => _ = new SimpleTriggerAttribute("00:10:00") { RepeatCount = -2 };

        act.Should().Throw<ArgumentOutOfRangeException>("SimpleTriggerImpl refuses the same count, later and further from where it was written")
            .WithMessage("RepeatCount cannot be -2: it counts the firings after the first, so it is 0 or more, or -1 to repeat forever.*")
            .Which.ParamName.Should().Be("RepeatCount");
    }

    [Test]
    public void EveryPropertyIsReadBackFromTheDeclaration()
    {
        QuartzJobAttribute job = typeof(DeclaredCleanupJob).GetCustomAttribute<QuartzJobAttribute>()!;

        job.Should().NotBeNull();
        job.Name.Should().Be("cleanup");
        job.Group.Should().Be("maintenance");
        job.Description.Should().Be("removes rows nobody reads");
        job.Durable.Should().BeTrue();
        job.RequestRecovery.Should().BeTrue();
        job.Scheduler.Should().Be("housekeeping");

        CronTriggerAttribute[] schedules = [.. typeof(DeclaredCleanupJob).GetCustomAttributes<CronTriggerAttribute>()];

        schedules.Should().HaveCount(2, "AllowMultiple is what lets one job declare several schedules");

        CronTriggerAttribute noon = schedules.Single(x => x.Name == "cleanup-weekday-noon");
        noon.CronExpression.Should().Be("0 0 12 ? * MON-FRI");
        noon.Group.Should().Be("housekeeping");
        noon.TimeZone.Should().Be("Europe/Helsinki");
        noon.MisfireInstruction.Should().Be(CronTriggerMisfireInstruction.DoNothing);
        noon.Priority.Should().Be(9);
        noon.Description.Should().Be("every weekday at noon, Helsinki time");
        noon.ExecutionGroup.Should().Be("maintenance");
        noon.ConfigurationKey.Should().Be("Jobs:Cleanup:Noon");

        SimpleTriggerAttribute interval = typeof(DeclaredCleanupJob).GetCustomAttributes<SimpleTriggerAttribute>().Should().ContainSingle().Subject;
        interval.Interval.Should().Be(TimeSpan.FromMinutes(30));
        interval.RepeatCount.Should().Be(11);
        interval.Name.Should().Be("cleanup-half-hourly");
        interval.Group.Should().Be("housekeeping");
        interval.MisfireInstruction.Should().Be(SimpleTriggerMisfireInstruction.NextWithRemainingCount);
        interval.Priority.Should().Be(4);
        interval.Description.Should().Be("every half hour, twelve times");
        interval.ExecutionGroup.Should().Be("maintenance");
        interval.ConfigurationKey.Should().Be("Jobs:Cleanup:Interval");
    }

    /// <summary>
    /// The usage the generator's reading of these attributes rests on.
    /// </summary>
    [Test]
    public void UsageIsClassesOnly()
    {
        AttributeUsageAttribute job = typeof(QuartzJobAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        job.ValidOn.Should().Be(AttributeTargets.Class);
        job.AllowMultiple.Should().BeFalse("a class is one job");
        job.Inherited.Should().BeFalse("a base class declaring a job would declare it again for every class deriving from it, under one key");

        AttributeUsageAttribute schedule = typeof(CronTriggerAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        schedule.ValidOn.Should().Be(AttributeTargets.Class);
        schedule.AllowMultiple.Should().BeTrue("a job may run on more than one schedule");
        schedule.Inherited.Should().BeFalse();

        AttributeUsageAttribute interval = typeof(SimpleTriggerAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        interval.ValidOn.Should().Be(AttributeTargets.Class);
        interval.AllowMultiple.Should().BeTrue("a job may run on more than one interval, beside its cron schedules");
        interval.Inherited.Should().BeFalse();
    }

    [QuartzJob(
        Name = "cleanup",
        Group = "maintenance",
        Description = "removes rows nobody reads",
        Durable = true,
        RequestRecovery = true,
        Scheduler = "housekeeping")]
    [CronTrigger("0 0 0/6 * * ?")]
    [CronTrigger(
        "0 0 12 ? * MON-FRI",
        Name = "cleanup-weekday-noon",
        Group = "housekeeping",
        TimeZone = "Europe/Helsinki",
        MisfireInstruction = CronTriggerMisfireInstruction.DoNothing,
        Priority = 9,
        Description = "every weekday at noon, Helsinki time",
        ExecutionGroup = "maintenance",
        ConfigurationKey = "Jobs:Cleanup:Noon")]
    [SimpleTrigger(
        "00:30:00",
        RepeatCount = 11,
        Name = "cleanup-half-hourly",
        Group = "housekeeping",
        MisfireInstruction = SimpleTriggerMisfireInstruction.NextWithRemainingCount,
        Priority = 4,
        Description = "every half hour, twelve times",
        ExecutionGroup = "maintenance",
        ConfigurationKey = "Jobs:Cleanup:Interval")]
    private sealed class DeclaredCleanupJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
