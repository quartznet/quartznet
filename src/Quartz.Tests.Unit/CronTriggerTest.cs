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

using NUnit.Framework;

using Quartz.Impl.Triggers;
using Quartz.Spi;

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

/// <summary>
/// Tests for CronTrigger.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[TestFixture]
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
        trigger.Name = "Quartz-579";
        trigger.Group = SchedulerConstants.DefaultGroup;
        trigger.TimeZone = tz;
        trigger.CronExpressionString = "0 0 12 * * ?";
        Assert.AreEqual(tz, trigger.TimeZone, "TimeZone was changed");
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
        trigger.Name = "Quartz-Custom";
        trigger.Group = SchedulerConstants.DefaultGroup;
        trigger.TimeZone = tz;
        trigger.CronExpressionString = "0 50 5,11,17,23 ? * *";
        trigger.StartTimeUtc = startDate;

        Assert.AreEqual(expectedFire, trigger.GetFireTimeAfter(startDate), $"Expected to fire at {expectedFire}");
        Assert.IsTrue(trigger.WillFireOn(expectedFire), $"Expected to fire at {expectedFire}");
        Assert.IsTrue(trigger.WillFireOn(expectedFire.AddHours(6)), $"Expected to fire at {expectedFire}");
        Assert.IsTrue(trigger.WillFireOn(expectedFire.AddHours(12)), $"Expected to fire at {expectedFire}");
        Assert.IsTrue(trigger.WillFireOn(expectedFire.AddHours(18)), $"Expected to fire at {expectedFire}");
        Assert.IsTrue(trigger.WillFireOn(expectedFire.AddHours(24)), $"Expected to fire at {expectedFire}");
    }

    [Test]
    public void BasicCronTriggerTest()
    {
        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Name = "Quartz-Sample";
        trigger.Group = SchedulerConstants.DefaultGroup;
        trigger.CronExpressionString = "0 0 12 1 1 ? 2099";
        trigger.StartTimeUtc = new DateTimeOffset(2099, 1, 1, 12, 0, 1, TimeSpan.Zero);
        trigger.EndTimeUtc = new DateTimeOffset(2099, 1, 1, 12, 0, 1, TimeSpan.Zero);

        Assert.IsNull(trigger.ComputeFirstFireTimeUtc(null));
    }

    [Test]
    public void TestPrecision()
    {
        IOperableTrigger trigger = new CronTriggerImpl();
        trigger.StartTimeUtc = new DateTime(1982, 6, 28, 13, 5, 5, 233);
        Assert.IsFalse(trigger.HasMillisecondPrecision);
        Assert.AreEqual(0, trigger.StartTimeUtc.Millisecond);
    }

    [Test]
    public void TestClone()
    {
        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Name = "test";
        trigger.Group = "testGroup";
        trigger.CronExpressionString = "0 0 12 * * ?";
        ICronTrigger trigger2 = (ICronTrigger) trigger.Clone();

        Assert.AreEqual(trigger, trigger2, "Cloning failed");

        // equals() doesn't test the cron expression
        Assert.AreEqual("0 0 12 * * ?", trigger2.CronExpressionString, "Cloning failed for the cron expression");
    }

    // http://jira.opensymphony.com/browse/QUARTZ-558
    [Test]
    public void TestQuartz558()
    {
        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Name = "test";
        trigger.Group = "testGroup";
        ICronTrigger trigger2 = (ICronTrigger) trigger.Clone();

        Assert.AreEqual(trigger, trigger2, "Cloning failed");
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

        Assert.That(trigger.StartTimeUtc, Is.EqualTo(trigger2.StartTimeUtc));
        Assert.That(trigger.EndTimeUtc, Is.EqualTo(trigger2.EndTimeUtc));
        Assert.That(trigger.Priority, Is.EqualTo(trigger2.Priority));
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
        Assert.That(cloned.MisfireInstruction, Is.EqualTo(trigger.MisfireInstruction));
        Assert.That(cloned.TimeZone, Is.EqualTo(trigger.TimeZone));
        Assert.That(cloned.CronExpressionString, Is.EqualTo(trigger.CronExpressionString));
    }

    [Test]
    public void TriggerWithBothStartAndEndDatesInPastShouldNotSchedule()
    {
        // Arrange: Create a trigger with both start and end dates in the past
        DateTimeOffset startDate = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset endDate = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string cronExpression = "0 30 14 ? * MON,TUE,WED,THU,FRI *"; // Weekdays at 14:30

        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Name = "PastTrigger";
        trigger.Group = SchedulerConstants.DefaultGroup;
        trigger.CronExpressionString = cronExpression;
        trigger.StartTimeUtc = startDate;
        trigger.EndTimeUtc = endDate;
        trigger.MisfireInstruction = MisfireInstruction.CronTrigger.FireOnceNow;

        // Act: Compute the first fire time
        DateTimeOffset? firstFireTime = trigger.ComputeFirstFireTimeUtc(null);

        // Assert: Should return null because the end date is in the past
        Assert.IsNull(firstFireTime, "Trigger with end date in the past should not schedule any fire time");
        Assert.IsFalse(trigger.GetMayFireAgain(), "Trigger should not fire again when end date is in the past");
    }

    [Test]
    public void TriggerWithStartDateInPastButEndDateInFutureShouldSchedule()
    {
        // Arrange: Create a trigger with start date in past but end date in future
        DateTimeOffset startDate = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset endDate = DateTimeOffset.UtcNow.AddYears(1);
        string cronExpression = "0 30 14 ? * MON,TUE,WED,THU,FRI *"; // Weekdays at 14:30

        CronTriggerImpl trigger = new CronTriggerImpl();
        trigger.Name = "ValidTrigger";
        trigger.Group = SchedulerConstants.DefaultGroup;
        trigger.CronExpressionString = cronExpression;
        trigger.StartTimeUtc = startDate;
        trigger.EndTimeUtc = endDate;

        // Act: Compute the first fire time
        DateTimeOffset? firstFireTime = trigger.ComputeFirstFireTimeUtc(null);

        // Assert: Should return a valid fire time (may be in the past between startDate and now, which would trigger misfire handling)
        Assert.IsNotNull(firstFireTime, "Trigger with future end date should schedule a fire time");
        Assert.IsTrue(firstFireTime.Value >= startDate, "Fire time should be on or after start date");
        Assert.IsTrue(firstFireTime.Value <= endDate, "Fire time should be before end date");
        Assert.IsTrue(trigger.GetMayFireAgain(), "Trigger should be able to fire again");

        // Verify that we can get a future fire time using GetFireTimeAfter
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? nextFireTime = trigger.GetFireTimeAfter(now);
        Assert.IsNotNull(nextFireTime, "Should be able to get next fire time after now");
        Assert.IsTrue(nextFireTime.Value > now, "Next fire time should be in the future");
        Assert.IsTrue(nextFireTime.Value <= endDate, "Next fire time should be before end date");
    }

    [Test]
    public void TriggerWithEndDateEqualToStartDateShouldNotSchedule()
    {
        // Arrange: Create a trigger where start and end dates are the same (in the past)
        var sameDate = new DateTimeOffset(2020, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var cronExpression = "0 0 12 * * ?"; // Daily at noon

        var trigger = new CronTriggerImpl
        {
            Name = "SameDateTrigger",
            Group = SchedulerConstants.DefaultGroup,
            CronExpressionString = cronExpression,
            StartTimeUtc = sameDate,
            EndTimeUtc = sameDate
        };

        // Act: Compute the first fire time
        var firstFireTime = trigger.ComputeFirstFireTimeUtc(null);

        Assert.IsNull(firstFireTime, "ComputeFirstFireTimeUtc should return null when EndTimeUtc equals StartTimeUtc");
    }
}