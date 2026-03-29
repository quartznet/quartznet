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

using Quartz.Impl.Triggers;
using Quartz.Spi;

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

/// <summary>
/// Tests for CronTrigger.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[NonParallelizable]
public class CronTriggerTest
{
    /// <summary>
    /// Tests the cron trigger time zone should change when changed.
    /// </summary>
    [Test]
    [Category("windowstimezoneid")]
    public void TestCronTriggerTimeZone_TimeZoneShouldChangeWhenChanged()
    {
        string tzStr = "FLE Standard Time";
        if (TimeZoneInfo.Local.Id == tzStr)
        {
            tzStr = "GMT Standard Time";
        }
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo(tzStr);
        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Key = new TriggerKey("Quartz-579", SchedulerConstants.DefaultGroup);
        trigger.TimeZone = tz;
        trigger.CronExpressionString = "0 0 12 * * ?";
        Assert.That(trigger.TimeZone, Is.EqualTo(tz), "TimeZone was changed");
    }

    [Test]
    [Category("windowstimezoneid")]
    public void TestCronTriggerTimeZoneWillFire()
    {
        string tzId = "GMT Standard Time";
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo(tzId);
        TimeSpan tzOffset = tz.BaseUtcOffset;
        DateTimeOffset startDate = new DateTimeOffset(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), tzOffset);
        DateTimeOffset expectedFire = startDate.AddHours(5).AddMinutes(50);

        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Key = new TriggerKey("Quartz-Custom", SchedulerConstants.DefaultGroup);
        trigger.TimeZone = tz;
        trigger.CronExpressionString = "0 50 5,11,17,23 ? * *";
        trigger.StartTimeUtc = startDate;

        Assert.Multiple(() =>
        {
            Assert.That(trigger.GetFireTimeAfter(startDate), Is.EqualTo(expectedFire), $"Expected to fire at {expectedFire}");
            Assert.That(trigger.WillFireOn(expectedFire), Is.True, $"Expected to fire at {expectedFire}");
            Assert.That(trigger.WillFireOn(expectedFire.AddHours(6)), Is.True, $"Expected to fire at {expectedFire}");
            Assert.That(trigger.WillFireOn(expectedFire.AddHours(12)), Is.True, $"Expected to fire at {expectedFire}");
            Assert.That(trigger.WillFireOn(expectedFire.AddHours(18)), Is.True, $"Expected to fire at {expectedFire}");
            Assert.That(trigger.WillFireOn(expectedFire.AddHours(24)), Is.True, $"Expected to fire at {expectedFire}");
        });
    }

    [Test]
    public void BasicCronTriggerTest()
    {
        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Key = new TriggerKey("Quartz-Sample", SchedulerConstants.DefaultGroup);
        trigger.CronExpressionString = "0 0 12 1 1 ? 2099";
        trigger.StartTimeUtc = new DateTimeOffset(2099, 1, 1, 12, 0, 1, TimeSpan.Zero);
        trigger.EndTimeUtc = new DateTimeOffset(2099, 1, 1, 12, 0, 1, TimeSpan.Zero);

        Assert.That(trigger.ComputeFirstFireTimeUtc(null), Is.Null);
    }

    [Test]
    public void TestPrecision()
    {
        IOperableTrigger trigger = new CronTriggerImpl();
        trigger.StartTimeUtc = new DateTime(1982, 6, 28, 13, 5, 5, 233);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.HasMillisecondPrecision, Is.False);
            Assert.That(trigger.StartTimeUtc.Millisecond, Is.EqualTo(0));
        });
    }

    [Test]
    public void TestClone()
    {
        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Key = new TriggerKey("test", "testGroup");
        trigger.CronExpressionString = "0 0 12 * * ?";
        ICronTrigger trigger2 = (ICronTrigger) trigger.Clone();
        Assert.Multiple(() =>
        {
            Assert.That(trigger2, Is.EqualTo(trigger), "Cloning failed");

            // equals() doesn't test the cron expression
            Assert.That(trigger2.CronExpressionString, Is.EqualTo("0 0 12 * * ?"), "Cloning failed for the cron expression");
        });
    }

    // http://jira.opensymphony.com/browse/QUARTZ-558
    [Test]
    public void TestQuartz558()
    {
        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Key = new TriggerKey("test", "testGroup");
        ICronTrigger trigger2 = (ICronTrigger) trigger.Clone();

        Assert.That(trigger2, Is.EqualTo(trigger), "Cloning failed");
    }

    [Test]
    public void TestMisfireInstructionValidity()
    {
        CronTriggerImpl trigger = new CronTriggerImpl();

        try
        {
            trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            trigger.MisfireInstruction = MisfireInstruction.SmartPolicy;
            trigger.MisfireInstruction = MisfireInstruction.CronTrigger.DoNothing;
            trigger.MisfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;
        }
        catch (Exception)
        {
            Assert.Fail("Unexpected exception while setting misfire instruction.");
        }

        try
        {
            trigger.MisfireInstruction = MisfireInstruction.CronTrigger.DoNothing + 1;

            Assert.Fail("Expected exception while setting invalid misfire instruction but did not get it.");
        }
        catch (Exception ex)
        {
            if (ex is AssertionException)
            {
                throw;
            }
        }
    }

    [Test]
    public void ShouldBeAbleToGetUnderlyingTriggerBuilder()
    {
        DateTimeOffset startTime = new DateTimeOffset(2012, 12, 30, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset endTime = new DateTimeOffset(2015, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var trigger = TriggerBuilder.Create().WithCronSchedule("0 0 12 * * ?")
            .StartAt(startTime)
            .EndAt(endTime)
            .WithPriority(2)
            .Build();
        var triggerBuilder = trigger.GetTriggerBuilder();
        var trigger2 = triggerBuilder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(trigger2.StartTimeUtc));
            Assert.That(trigger.EndTimeUtc, Is.EqualTo(trigger2.EndTimeUtc));
            Assert.That(trigger.Priority, Is.EqualTo(trigger2.Priority));
        });
    }

    [Test]
    public void ShouldGetScheduleBuilderWithSameSettingsAsTrigger()
    {
        var startTime = DateTimeOffset.UtcNow;
        var endTime = DateTimeOffset.UtcNow.AddDays(1);
        var trigger = new CronTriggerImpl("name", "group", "jobname", "jobgroup", startTime, endTime, "0 0 12 * * ?", TimeZoneInfo.Utc);
        trigger.MisfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;
        var scheduleBuilder = trigger.GetScheduleBuilder();

        var cloned = (CronTriggerImpl) scheduleBuilder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(cloned.MisfireInstruction, Is.EqualTo(trigger.MisfireInstruction));
            Assert.That(cloned.TimeZone, Is.EqualTo(trigger.TimeZone));
            Assert.That(cloned.CronExpressionString, Is.EqualTo(trigger.CronExpressionString));
        });
    }

    [Test]
    public void TriggerWithBothStartAndEndDatesInPastShouldNotSchedule()
    {
        DateTimeOffset startDate = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset endDate = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string cronExpression = "0 30 14 ? * MON,TUE,WED,THU,FRI *";

        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Key = new TriggerKey("PastTrigger", SchedulerConstants.DefaultGroup);
        trigger.CronExpressionString = cronExpression;
        trigger.StartTimeUtc = startDate;
        trigger.EndTimeUtc = endDate;
        trigger.MisfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;

        DateTimeOffset? firstFireTime = trigger.ComputeFirstFireTimeUtc(null);

        Assert.That(firstFireTime, Is.Null, "Trigger with end date in the past should not schedule any fire time");
        Assert.That(trigger.GetMayFireAgain(), Is.False, "Trigger should not fire again when end date is in the past");
    }

    [Test]
    public void TriggerWithStartDateInPastButEndDateInFutureShouldSchedule()
    {
        DateTimeOffset startDate = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset endDate = DateTimeOffset.UtcNow.AddYears(1);
        string cronExpression = "0 30 14 ? * MON,TUE,WED,THU,FRI *";

        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Key = new TriggerKey("ValidTrigger", SchedulerConstants.DefaultGroup);
        trigger.CronExpressionString = cronExpression;
        trigger.StartTimeUtc = startDate;
        trigger.EndTimeUtc = endDate;

        DateTimeOffset? firstFireTime = trigger.ComputeFirstFireTimeUtc(null);

        Assert.That(firstFireTime, Is.Not.Null, "Trigger with future end date should schedule a fire time");
        Assert.That(firstFireTime!.Value >= DateTimeOffset.UtcNow, Is.True, "Fire time should be in the future");
        Assert.That(firstFireTime.Value <= endDate, Is.True, "Fire time should be before end date");
        Assert.That(trigger.GetMayFireAgain(), Is.True, "Trigger should be able to fire again");

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? nextFireTime = trigger.GetFireTimeAfter(now);
        Assert.That(nextFireTime, Is.Not.Null, "Should be able to get next fire time after now");
        Assert.That(nextFireTime!.Value > now, Is.True, "Next fire time should be in the future");
        Assert.That(nextFireTime.Value <= endDate, Is.True, "Next fire time should be before end date");
    }

    [Test]
    public void TriggerWithEndDateEqualToStartDateShouldNotSchedule()
    {
        var sameDate = new DateTimeOffset(2020, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var cronExpression = "0 0 12 * * ?";

        var trigger = new CronTriggerImpl
        {
            Key = new TriggerKey("SameDateTrigger", SchedulerConstants.DefaultGroup),
            CronExpressionString = cronExpression,
            StartTimeUtc = sameDate,
            EndTimeUtc = sameDate
        };

        var firstFireTime = trigger.ComputeFirstFireTimeUtc(null);

        Assert.That(firstFireTime, Is.Null, "ComputeFirstFireTimeUtc should return null when EndTimeUtc equals StartTimeUtc");
    }

    [Test]
    public void RescheduledTriggerWithOldStartTimeShouldNotFireInPast()
    {
        // Simulate the issue from GitHub #764: rebuild a trigger via GetTriggerBuilder().Build()
        // after it has been running. The rebuilt trigger should not compute a fire time in the past.
        var pastStartTime = DateTimeOffset.UtcNow.AddHours(-1);

        var originalTrigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithCronSchedule("0 */5 * ? * *",
                cs => cs.WithMisfireHandlingInstructionDoNothing())
            .StartAt(pastStartTime)
            .Build();

        // Rebuild via GetTriggerBuilder (preserves old StartTimeUtc, truncated to seconds by cron)
        var rebuilt = (IOperableTrigger) originalTrigger.GetTriggerBuilder().Build();
        Assert.That(rebuilt.StartTimeUtc, Is.LessThan(DateTimeOffset.UtcNow));

        var firstFireTime = rebuilt.ComputeFirstFireTimeUtc(null);

        Assert.That(firstFireTime, Is.Not.Null);
        Assert.That(firstFireTime!.Value, Is.GreaterThanOrEqualTo(DateTimeOffset.UtcNow),
            "Rebuilt trigger's first fire time must not be in the past");
    }

    [Test]
    public void TriggerWithFutureStartTimeIsUnaffectedByPastGuard()
    {
        var futureStart = DateTimeOffset.UtcNow.AddHours(1);

        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithCronSchedule("0 */5 * ? * *")
            .StartAt(futureStart)
            .Build();

        var firstFireTime = trigger.ComputeFirstFireTimeUtc(null);

        Assert.That(firstFireTime, Is.Not.Null);
        Assert.That(firstFireTime!.Value, Is.GreaterThanOrEqualTo(futureStart),
            "Fire time should be on or after the future start time");
    }

    [Test]
    public void DoNothing_WithMisfireThreshold_PreservesWithinThresholdFireTime()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);
        var threshold = TimeSpan.FromSeconds(60);

        var trigger = new CronTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            CronExpressionString = "0 0/2 * * * ?",
            StartTimeUtc = startTime,
            MisfireInstruction = MisfireInstruction.CronTrigger.DoNothing
        };
        trigger.ComputeFirstFireTimeUtc(null);

        trigger.UpdateAfterMisfire(null, threshold);

        DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 2, 0, TimeSpan.Zero)),
            "Should preserve the 10:02 fire time that is within the misfire threshold");
    }

    [Test]
    public void DoNothing_WithMisfireThreshold_SkipsGenuinelyMisfiredTimes()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 4, 30, TimeSpan.Zero);
        var threshold = TimeSpan.FromSeconds(60);

        var trigger = new CronTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            CronExpressionString = "0 0/2 * * * ?",
            StartTimeUtc = startTime,
            MisfireInstruction = MisfireInstruction.CronTrigger.DoNothing
        };
        trigger.ComputeFirstFireTimeUtc(null);

        trigger.UpdateAfterMisfire(null, threshold);

        DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 4, 0, TimeSpan.Zero)),
            "Should advance to 10:04 which is within the threshold window");
    }

    [Test]
    public void DoNothing_WithoutThreshold_SkipsAllPastFireTimes()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);

        var trigger = new CronTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            CronExpressionString = "0 0/2 * * * ?",
            StartTimeUtc = startTime,
            MisfireInstruction = MisfireInstruction.CronTrigger.DoNothing
        };
        trigger.ComputeFirstFireTimeUtc(null);

        trigger.UpdateAfterMisfire(null);

        DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 4, 0, TimeSpan.Zero)),
            "Without threshold, should skip to next fire time after now");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}