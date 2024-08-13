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

using FakeItEasy;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit;

/// <summary>
/// Unit test for SimpleTrigger serialization backwards compatibility.
/// </summary>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
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
            EndTime, 5, TimeSpan.FromSeconds(1))
        {
            CalendarName = "MyCalendar",
            Description = "SimpleTriggerDesc",
            JobDataMap = jobDataMap,
            MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount
        };

        return t;
    }

    protected override void VerifyMatch(SimpleTriggerImpl original, SimpleTriggerImpl deserialized)
    {
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Key, Is.EqualTo(original.Key));
            Assert.That(deserialized.JobKey, Is.EqualTo(original.JobKey));
            Assert.That(deserialized.StartTimeUtc, Is.EqualTo(original.StartTimeUtc));
            Assert.That(deserialized.EndTimeUtc, Is.EqualTo(original.EndTimeUtc));
            Assert.That(deserialized.RepeatCount, Is.EqualTo(original.RepeatCount));
            Assert.That(deserialized.RepeatInterval, Is.EqualTo(original.RepeatInterval));
            Assert.That(deserialized.CalendarName, Is.EqualTo(original.CalendarName));
            Assert.That(deserialized.Description, Is.EqualTo(original.Description));
            Assert.That(deserialized.JobDataMap, Is.EqualTo(original.JobDataMap));
            Assert.That(deserialized.MisfireInstruction, Is.EqualTo(original.MisfireInstruction));
        });
    }

    [Test]
    public void TestUpdateAfterMisfire()
    {
        DateTimeOffset startTime = new DateTimeOffset(2005, 7, 5, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset endTime = new DateTimeOffset(2005, 7, 5, 10, 0, 0, TimeSpan.Zero);

        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl
        {
            MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount,
            RepeatCount = 5,
            StartTimeUtc = startTime,
            EndTimeUtc = endTime
        };

        simpleTrigger.UpdateAfterMisfire(null);
        Assert.Multiple(() =>
        {
            Assert.That(simpleTrigger.StartTimeUtc, Is.EqualTo(startTime));
            Assert.That(simpleTrigger.EndTimeUtc.Value, Is.EqualTo(endTime));
            Assert.That(!simpleTrigger.GetNextFireTimeUtc().HasValue, Is.True);
        });
    }

    [Test]
    public void TestGetFireTimeAfter()
    {
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl();

        DateTimeOffset startTime = DateBuilder.EvenSecondDate(DateTime.UtcNow);

        simpleTrigger.StartTimeUtc = startTime;
        simpleTrigger.RepeatInterval = TimeSpan.FromMilliseconds(10);
        simpleTrigger.RepeatCount = 4;

        var fireTimeAfter = simpleTrigger.GetFireTimeAfter(startTime.AddMilliseconds(34));
        Assert.That(fireTimeAfter.Value, Is.EqualTo(startTime.AddMilliseconds(40)));
    }

    [Test]
    public void TestClone()
    {
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl();

        // Make sure empty sub-objects are cloned okay
        ITrigger clone = simpleTrigger.Clone();
        Assert.That(clone.JobDataMap, Is.Empty);

        // Make sure non-empty sub-objects are cloned okay
        simpleTrigger.JobDataMap.Put("K1", "V1");
        simpleTrigger.JobDataMap.Put("K2", "V2");
        clone = simpleTrigger.Clone();
        Assert.Multiple(() =>
        {
            Assert.That(clone.JobDataMap, Has.Count.EqualTo(2));
            Assert.That(clone.JobDataMap["K1"], Is.EqualTo("V1"));
            Assert.That(clone.JobDataMap["K2"], Is.EqualTo("V2"));
        });

        // Make sure sub-object collections have really been cloned by ensuring
        // their modification does not change the source Trigger
        clone.JobDataMap.Remove("K1");
        Assert.Multiple(() =>
        {
            Assert.That(clone.JobDataMap, Has.Count.EqualTo(1));
            Assert.That(simpleTrigger.JobDataMap, Has.Count.EqualTo(2));
            Assert.That(simpleTrigger.JobDataMap["K1"], Is.EqualTo("V1"));
            Assert.That(simpleTrigger.JobDataMap["K2"], Is.EqualTo("V2"));
        });
        
    }

    // QRTZNET-73
    [Test]
    public void TestGetFireTimeAfter_WithCalendar()
    {
        DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50");
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl
        {
            RepeatInterval = TimeSpan.FromMilliseconds(10),
            RepeatCount = 1
        };
        DateTimeOffset neverFireTime = DateBuilder.EvenMinuteDateBefore(dailyCalendar.GetTimeRangeStartingTimeUtc(DateTime.Now));
        simpleTrigger.StartTimeUtc = neverFireTime;

        simpleTrigger.ComputeFirstFireTimeUtc(dailyCalendar);
        DateTimeOffset? fireTimeAfter = simpleTrigger.GetNextFireTimeUtc();

        Assert.That(fireTimeAfter, Is.Null);
    }

    [Test]
    public void TestPrecision()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl();
        trigger.StartTimeUtc = new DateTimeOffset(1982, 6, 28, 13, 5, 5, 233, TimeSpan.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(trigger.HasMillisecondPrecision, Is.True);
            Assert.That(trigger.StartTimeUtc.Millisecond, Is.EqualTo(233));
        });
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
}