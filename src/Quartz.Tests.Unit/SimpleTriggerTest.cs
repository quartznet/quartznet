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

using System.Collections.Specialized;

using FakeItEasy;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit;

/// <summary>
/// Unit test for SimpleTrigger serialization backwards compatibility.
/// </summary>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
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
        jobDataMap["A"] = "B";

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
        simpleTrigger.JobDataMap["K1"] = "V1";
        simpleTrigger.JobDataMap["K2"] = "V2";
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
        var referenceDate = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset neverFireTime = DateBuilder.EvenMinuteDateBefore(dailyCalendar.GetTimeRangeStartingTimeUtc(referenceDate));
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

    /// <summary>
    /// Regression test for #2455: When a SimpleTrigger is created with a StartTimeUtc
    /// in the past and later scheduled, the first fire time should be in the future.
    /// </summary>
    [Test]
    public async Task ScheduleJob_WhenStartTimeInPast_ShouldFireInFuture()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset pastStart = now.AddHours(-1);

        var config = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "SimpleTriggerPastStartTest",
            ["quartz.timeProvider.type"] = typeof(FixedTimeProvider).AssemblyQualifiedName!,
        };
        FixedTimeProvider.UtcNowValue = now;

        IScheduler scheduler = await new StdSchedulerFactory(config).GetScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("testJob", "testGroup")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .StartAt(pastStart)
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(10)
                    .RepeatForever())
                .Build();

            DateTimeOffset firstFire = await scheduler.ScheduleJob(job, trigger);

            Assert.That(firstFire, Is.GreaterThanOrEqualTo(now),
                "First fire time should not be in the past when trigger has never fired");

            ITrigger storedTrigger = await scheduler.GetTrigger(trigger.Key);
            Assert.IsNotNull(storedTrigger);
            Assert.That(storedTrigger.GetNextFireTimeUtc(), Is.GreaterThanOrEqualTo(now),
                "Stored trigger's next fire time should not be in the past");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// Regression test for #2455: When a SimpleTrigger with a finite repeat count is
    /// created with a StartTimeUtc in the past and later scheduled, the first fire
    /// time should be in the future and the repeat count should still be honored.
    /// </summary>
    [Test]
    public async Task ScheduleJob_WhenStartTimeInPast_WithFiniteRepeatCount_ShouldFireInFuture()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset pastStart = now.AddMinutes(-25);

        var config = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "SimpleTriggerPastStartFiniteTest",
            ["quartz.timeProvider.type"] = typeof(FixedTimeProvider).AssemblyQualifiedName!,
        };
        FixedTimeProvider.UtcNowValue = now;

        IScheduler scheduler = await new StdSchedulerFactory(config).GetScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("testJob", "testGroup")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .StartAt(pastStart)
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(10)
                    .WithRepeatCount(5))
                .Build();

            DateTimeOffset firstFire = await scheduler.ScheduleJob(job, trigger);

            Assert.That(firstFire, Is.GreaterThanOrEqualTo(now),
                "First fire time should not be in the past");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// Regression test for #2455: A SimpleTrigger with a future StartTimeUtc should
    /// not be affected by the past-start-time adjustment.
    /// </summary>
    [Test]
    public async Task ScheduleJob_WhenStartTimeInFuture_ShouldNotChange()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset futureStart = now.AddHours(1);

        var config = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "SimpleTriggerFutureStartTest",
            ["quartz.timeProvider.type"] = typeof(FixedTimeProvider).AssemblyQualifiedName!,
        };
        FixedTimeProvider.UtcNowValue = now;

        IScheduler scheduler = await new StdSchedulerFactory(config).GetScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("testJob", "testGroup")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .StartAt(futureStart)
                .WithSimpleSchedule(x => x
                    .WithIntervalInMinutes(10)
                    .RepeatForever())
                .Build();

            DateTimeOffset firstFire = await scheduler.ScheduleJob(job, trigger);

            Assert.That(firstFire, Is.EqualTo(futureStart),
                "First fire time should be the original start time when it is in the future");
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    /// <summary>
    /// Regression test for #2455: A non-repeating SimpleTrigger with a past StartTimeUtc
    /// should not be affected by the adjustment (it should retain original behavior).
    /// </summary>
    [Test]
    public async Task ScheduleJob_NonRepeating_WhenStartTimeInPast_ShouldRetainOriginalBehavior()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset pastStart = now.AddHours(-1);

        var config = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
            ["quartz.scheduler.instanceName"] = "SimpleTriggerNonRepeatingTest",
            ["quartz.timeProvider.type"] = typeof(FixedTimeProvider).AssemblyQualifiedName!,
        };
        FixedTimeProvider.UtcNowValue = now;

        IScheduler scheduler = await new StdSchedulerFactory(config).GetScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("testJob", "testGroup")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .StartAt(pastStart)
                .WithSimpleSchedule(x => x
                    .WithRepeatCount(0))
                .Build();

            DateTimeOffset firstFire = await scheduler.ScheduleJob(job, trigger);

            // Non-repeating trigger should retain the past start time
            // (misfire handling will deal with it later)
            Assert.That(firstFire, Is.EqualTo(pastStart));
        }
        finally
        {
            await scheduler.Shutdown(false);
        }
    }

    [Test]
    public void RescheduleNextWithExistingCount_WithMisfireThreshold_PreservesWithinThresholdFireTime()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);
        var threshold = TimeSpan.FromSeconds(60);

        var trigger = new SimpleTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = startTime,
            RepeatInterval = TimeSpan.FromMinutes(2),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount
        };
        trigger.ComputeFirstFireTimeUtc(null);

        trigger.UpdateAfterMisfire(null, threshold);

        DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 2, 0, TimeSpan.Zero)),
            "Should preserve the 10:02 fire time that is within the misfire threshold");
    }

    [Test]
    public void RescheduleNextWithRemainingCount_WithMisfireThreshold_PreservesWithinThresholdFireTime()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);
        var threshold = TimeSpan.FromSeconds(60);

        var trigger = new SimpleTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = startTime,
            RepeatInterval = TimeSpan.FromMinutes(2),
            RepeatCount = 10,
            MisfireInstruction = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount
        };
        trigger.ComputeFirstFireTimeUtc(null);

        // Simulate that the trigger has already fired once at 10:00:00
        trigger.SetNextFireTimeUtc(startTime);
        trigger.TimesTriggered = 1;

        trigger.UpdateAfterMisfire(null, threshold);

        DateTimeOffset? nextFire = trigger.GetNextFireTimeUtc();
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 2, 0, TimeSpan.Zero)),
            "Should preserve the 10:02 fire time that is within the misfire threshold");
        // Only 1 fire time missed (10:00 -> 10:02), not 2 (10:00 -> 10:04)
        Assert.That(trigger.TimesTriggered, Is.EqualTo(2));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        /// <summary>
        /// Static value used by StdSchedulerFactory instantiation (parameterless constructor).
        /// </summary>
        internal static DateTimeOffset UtcNowValue;

        public FixedTimeProvider() : this(UtcNowValue) { }

        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}