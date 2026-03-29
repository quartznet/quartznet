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

using FakeItEasy;

using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit;

/// <summary>
/// Unit test for SimpleTrigger serialization backwards compatibility.
/// </summary>
[TestFixture(typeof(BinaryObjectSerializer))]
[TestFixture(typeof(JsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
[NonParallelizable]
public class SimpleTriggerTest : SerializationTestSupport<SimpleTriggerImpl>
{
    private static readonly DateTimeOffset StartTime;
    private static readonly DateTimeOffset EndTime;

    static SimpleTriggerTest()
    {
        StartTime = new DateTimeOffset(2006, 6, 1, 10, 5, 15, TimeSpan.Zero);
        // StartTime.setTimeZone(EST_TIME_ZONE);
        EndTime = new DateTimeOffset(2008, 5, 2, 20, 15, 30, TimeSpan.Zero);
        // EndTime.setTimeZone(EST_TIME_ZONE);
    }

    public SimpleTriggerTest(Type serializerType) : base(serializerType)
    {
    }

    /// <summary>
    /// Get the object to serialize when generating serialized file for future
    /// tests, and against which to validate deserialized object.
    /// </summary>
    /// <returns></returns>
    protected override SimpleTriggerImpl GetTargetObject()
    {
        JobDataMap jobDataMap = new JobDataMap();
        jobDataMap.Put("A", "B");

        SimpleTriggerImpl t = new SimpleTriggerImpl("SimpleTrigger", "SimpleGroup",
            "JobName", "JobGroup", StartTime,
            EndTime, 5, TimeSpan.FromSeconds(1));
        t.CalendarName = "MyCalendar";
        t.Description = "SimpleTriggerDesc";
        t.JobDataMap = jobDataMap;
        t.MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;

        return t;
    }

    protected override void VerifyMatch(SimpleTriggerImpl original, SimpleTriggerImpl deserialized)
    {
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Key, deserialized.Key);
        Assert.AreEqual(original.JobKey, deserialized.JobKey);
        Assert.AreEqual(original.StartTimeUtc, deserialized.StartTimeUtc);
        Assert.AreEqual(original.EndTimeUtc, deserialized.EndTimeUtc);
        Assert.AreEqual(original.RepeatCount, deserialized.RepeatCount);
        Assert.AreEqual(original.RepeatInterval, deserialized.RepeatInterval);
        Assert.AreEqual(original.CalendarName, deserialized.CalendarName);
        Assert.AreEqual(original.Description, deserialized.Description);
        Assert.AreEqual(original.JobDataMap, deserialized.JobDataMap);
        Assert.AreEqual(original.MisfireInstruction, deserialized.MisfireInstruction);
    }

    [Test]
    public void TestUpdateAfterMisfire()
    {
        DateTimeOffset startTime = new DateTimeOffset(2005, 7, 5, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset endTime = new DateTimeOffset(2005, 7, 5, 10, 0, 0, TimeSpan.Zero);

        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl();
        simpleTrigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
        simpleTrigger.RepeatCount = 5;
        simpleTrigger.StartTimeUtc = startTime;
        simpleTrigger.EndTimeUtc = endTime;

        simpleTrigger.UpdateAfterMisfire(null);
        Assert.AreEqual(startTime, simpleTrigger.StartTimeUtc);
        Assert.AreEqual(endTime, simpleTrigger.EndTimeUtc.Value);
        Assert.IsTrue(!simpleTrigger.GetNextFireTimeUtc().HasValue);
    }

    [Test]
    public void TestGetFireTimeAfter()
    {
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl();

        DateTimeOffset startTime = DateBuilder.EvenSecondDate(DateTime.UtcNow);

        simpleTrigger.StartTimeUtc = startTime;
        simpleTrigger.RepeatInterval = TimeSpan.FromMilliseconds(10);
        simpleTrigger.RepeatCount = 4;

        DateTimeOffset? fireTimeAfter;
        fireTimeAfter = simpleTrigger.GetFireTimeAfter(startTime.AddMilliseconds(34));
        Assert.AreEqual(startTime.AddMilliseconds(40), fireTimeAfter.Value);
    }

    [Test]
    public void TestClone()
    {
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl();

        // Make sure empty sub-objects are cloned okay
        ITrigger clone = simpleTrigger.Clone();
        Assert.AreEqual(0, clone.JobDataMap.Count);

        // Make sure non-empty sub-objects are cloned okay
        simpleTrigger.JobDataMap.Put("K1", "V1");
        simpleTrigger.JobDataMap.Put("K2", "V2");
        clone = simpleTrigger.Clone();
        Assert.AreEqual(2, clone.JobDataMap.Count);
        Assert.AreEqual("V1", clone.JobDataMap["K1"]);
        Assert.AreEqual("V2", clone.JobDataMap["K2"]);

        // Make sure sub-object collections have really been cloned by ensuring
        // their modification does not change the source Trigger
        clone.JobDataMap.Remove("K1");
        Assert.AreEqual(1, clone.JobDataMap.Count);

        Assert.AreEqual(2, simpleTrigger.JobDataMap.Count);
        Assert.AreEqual("V1", simpleTrigger.JobDataMap["K1"]);
        Assert.AreEqual("V2", simpleTrigger.JobDataMap["K2"]);
    }

    // QRTZNET-73
    [Test]
    public void TestGetFireTimeAfter_WithCalendar()
    {
        DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50");
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl();
        simpleTrigger.RepeatInterval = TimeSpan.FromMilliseconds(10);
        simpleTrigger.RepeatCount = 1;
        var referenceDate = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset neverFireTime = DateBuilder.EvenMinuteDateBefore(dailyCalendar.GetTimeRangeStartingTimeUtc(referenceDate));
        simpleTrigger.StartTimeUtc = neverFireTime;

        simpleTrigger.ComputeFirstFireTimeUtc(dailyCalendar);
        DateTimeOffset? fireTimeAfter = simpleTrigger.GetNextFireTimeUtc();

        Assert.IsNull(fireTimeAfter);
    }

    [Test]
    public void TestPrecision()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl();
        trigger.StartTimeUtc = new DateTimeOffset(1982, 6, 28, 13, 5, 5, 233, TimeSpan.Zero);
        Assert.IsTrue(trigger.HasMillisecondPrecision);
        Assert.AreEqual(233, trigger.StartTimeUtc.Millisecond);
    }

    [Test]
    public void TestMisfireInstructionValidity()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl();

        try
        {
            trigger.MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy;
            trigger.MisfireInstruction = MisfireInstruction.SmartPolicy;
            trigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.FireNow;
            trigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount;
            trigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;
            trigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
            trigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
        }
        catch (Exception)
        {
            Assert.Fail("Unexpected exception while setting misfire instruction.");
        }

        try
        {
            trigger.MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount + 1;

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
    public void ShouldRemoveTriggerIfNotGoingToFireAgain()
    {
        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithSimpleSchedule()
            .StartAt(DateTime.UtcNow.AddDays(-2))
            .EndAt(DateTime.UtcNow.AddDays(-1))
            .Build();

        var instruction = trigger.ExecutionComplete(A.Fake<IJobExecutionContext>(), new JobExecutionException());
        Assert.That(instruction, Is.EqualTo(SchedulerInstruction.DeleteTrigger));
    }

    [Test]
    public void RescheduleNextWithExistingCount_WithMisfireThreshold_PreservesWithinThresholdFireTime()
    {
        // Simple trigger: fires every 2 minutes starting at 10:00:00.
        // Scheduler recovers at 10:02:30. Threshold = 60s.
        // 10:02:00 is only 30s old (within threshold) -- should be preserved.
        var startTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);
        var threshold = TimeSpan.FromSeconds(60);

        var trigger = new SimpleTriggerImpl
        {
            Name = "test",
            Group = "test",
            StartTimeUtc = startTime,
            RepeatInterval = TimeSpan.FromMinutes(2),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount
        };
        trigger.ComputeFirstFireTimeUtc(null);

        var original = SystemTime.UtcNow;
        try
        {
            SystemTime.UtcNow = () => frozenNow;
            trigger.UpdateAfterMisfire(null, threshold);

            DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
            Assert.IsNotNull(nextFire);
            Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 2, 0, TimeSpan.Zero)),
                "Should preserve the 10:02 fire time that is within the misfire threshold");
        }
        finally
        {
            SystemTime.UtcNow = original;
        }
    }

    [Test]
    public void RescheduleNextWithRemainingCount_WithMisfireThreshold_PreservesWithinThresholdFireTime()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);
        var threshold = TimeSpan.FromSeconds(60);

        var trigger = new SimpleTriggerImpl
        {
            Name = "test",
            Group = "test",
            StartTimeUtc = startTime,
            RepeatInterval = TimeSpan.FromMinutes(2),
            RepeatCount = 10,
            MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount
        };
        trigger.ComputeFirstFireTimeUtc(null);

        // Simulate that the trigger has already fired once at 10:00:00
        trigger.SetNextFireTimeUtc(startTime);
        trigger.TimesTriggered = 1;

        var original = SystemTime.UtcNow;
        try
        {
            SystemTime.UtcNow = () => frozenNow;
            trigger.UpdateAfterMisfire(null, threshold);

            DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
            Assert.IsNotNull(nextFire);
            Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 2, 0, TimeSpan.Zero)),
                "Should preserve the 10:02 fire time that is within the misfire threshold");
            // Only 1 fire time missed (10:00 -> 10:02), not 2 (10:00 -> 10:04)
            Assert.That(trigger.TimesTriggered, Is.EqualTo(2));
        }
        finally
        {
            SystemTime.UtcNow = original;
        }
    }
}