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
using Quartz.Jobs;
using Quartz.Extensibility;

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
            MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount
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
            Assert.That(deserialized.MisfireInstructionCode, Is.EqualTo(original.MisfireInstructionCode));
        });
    }

    [Test]
    public void TestUpdateAfterMisfire()
    {
        DateTimeOffset startTime = new DateTimeOffset(2005, 7, 5, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset endTime = new DateTimeOffset(2005, 7, 5, 10, 0, 0, TimeSpan.Zero);

        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl
        {
            MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount,
            RepeatCount = 5,
            StartTimeUtc = startTime,
            EndTimeUtc = endTime
        };

        simpleTrigger.UpdateAfterMisfire(null);
        Assert.Multiple(() =>
        {
            Assert.That(simpleTrigger.StartTimeUtc, Is.EqualTo(startTime));
            Assert.That(simpleTrigger.EndTimeUtc.Value, Is.EqualTo(endTime));
            Assert.That(!simpleTrigger.NextFireTimeUtc.HasValue, Is.True);
        });
    }

    [Test]
    public void TestGetFireTimeAfter()
    {
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl();

        DateTimeOffset startTime = TestDates.EvenSecondDate(DateTime.UtcNow);

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
        DailyCalendar dailyCalendar = new DailyCalendar(new TimeOnly(1, 20), new TimeOnly(14, 50));
        SimpleTriggerImpl simpleTrigger = new SimpleTriggerImpl
        {
            RepeatInterval = TimeSpan.FromMilliseconds(10),
            RepeatCount = 1
        };
        var referenceDate = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset neverFireTime = TestDates.EvenMinuteDateBefore(dailyCalendar.GetTimeRangeStartingTimeUtc(referenceDate));
        simpleTrigger.StartTimeUtc = neverFireTime;

        simpleTrigger.ComputeFirstFireTimeUtc(dailyCalendar);
        DateTimeOffset? fireTimeAfter = simpleTrigger.NextFireTimeUtc;

        Assert.That(fireTimeAfter, Is.Null);
    }

    [Test]
    public void TestPrecision()
    {
        IOperableTrigger trigger = new SimpleTriggerImpl();
        trigger.StartTimeUtc = new DateTimeOffset(1982, 6, 28, 13, 5, 5, 233, TimeSpan.Zero);

        trigger.StartTimeUtc.Millisecond.Should().Be(233, "a simple trigger keeps millisecond precision in its start time");
    }

    [Test]
    public void TestMisfireInstructionValidity()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl();

        try
        {
            trigger.MisfireInstructionCode = MisfireInstruction.IgnoreMisfirePolicy;
            trigger.MisfireInstructionCode = MisfireInstruction.SmartPolicy;
            trigger.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.FireNow;
            trigger.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount;
            trigger.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;
            trigger.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
            trigger.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
        }
        catch (Exception)
        {
            Assert.Fail("Unexpected exception while setting misfire instruction.");
        }

        try
        {
            trigger.MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount + 1;

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

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("testJob", "testGroup")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .StartAt(pastStart)
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromMinutes(10))
                    .RepeatForever())
                .Build();

            DateTimeOffset firstFire = await scheduler.ScheduleJob(job, trigger);

            Assert.That(firstFire, Is.GreaterThanOrEqualTo(now),
                "First fire time should not be in the past when trigger has never fired");

            ITrigger storedTrigger = await scheduler.GetTrigger(trigger.Key);
            Assert.IsNotNull(storedTrigger);
            Assert.That(storedTrigger.NextFireTimeUtc, Is.GreaterThanOrEqualTo(now),
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

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("testJob", "testGroup")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .StartAt(pastStart)
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromMinutes(10))
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

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();
        try
        {
            IJobDetail job = JobBuilder.Create<NoOpJob>()
                .WithIdentity("testJob", "testGroup")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("testTrigger", "testGroup")
                .StartAt(futureStart)
                .WithSimpleSchedule(x => x
                    .WithInterval(TimeSpan.FromMinutes(10))
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

        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();
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
    public void RescheduleNextWithExistingCount_AfterMisfire_YieldsStrictlyFutureFireTime()
    {
        // Simple trigger: fires every 2 minutes starting at 10:00:00 (in the past).
        // Misfire handling runs at 10:02:30. The trigger must be rescheduled to the
        // next fire time strictly after 'now' (10:04:00) and must not fire immediately,
        // even though the 10:02:00 fire time would be within a typical misfire
        // threshold window (#3096).
        var startTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);

        var trigger = new SimpleTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = startTime,
            RepeatInterval = TimeSpan.FromMinutes(2),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount
        };
        trigger.ComputeFirstFireTimeUtc(null);

        trigger.UpdateAfterMisfire(null);

        DateTimeOffset? nextFire = trigger.NextFireTimeUtc;
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.GreaterThan(frozenNow),
            "Trigger must not fire immediately after misfire handling (#3096)");
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 4, 0, TimeSpan.Zero)),
            "Should reschedule to the next scheduled time strictly after now");
    }

    [Test]
    public void RescheduleNextWithRemainingCount_AfterMisfire_YieldsStrictlyFutureFireTime()
    {
        var startTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var frozenNow = new DateTimeOffset(2025, 1, 1, 10, 2, 30, TimeSpan.Zero);

        var trigger = new SimpleTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = startTime,
            RepeatInterval = TimeSpan.FromMinutes(2),
            RepeatCount = 10,
            MisfireInstructionCode = MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount
        };
        trigger.ComputeFirstFireTimeUtc(null);

        // Simulate that the trigger has already fired once at 10:00:00
        trigger.NextFireTimeUtc = startTime;
        trigger.TimesTriggered = 1;

        trigger.UpdateAfterMisfire(null);

        DateTimeOffset? nextFire = trigger.NextFireTimeUtc;
        Assert.IsNotNull(nextFire);
        Assert.That(nextFire.Value, Is.GreaterThan(frozenNow),
            "Trigger must not fire immediately after misfire handling (#3096)");
        Assert.That(nextFire.Value, Is.EqualTo(new DateTimeOffset(2025, 1, 1, 10, 4, 0, TimeSpan.Zero)),
            "Should reschedule to the next scheduled time strictly after now");
        // 2 fire times missed (10:00 and 10:02)
        Assert.That(trigger.TimesTriggered, Is.EqualTo(3));
    }

    private static readonly DateTimeOffset MisfireStartTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Misfire handling runs five minutes after the schedule started, so 10:00, 10:02 and 10:04 are
    /// all past due and 'now' sits between two fire times rather than on one.
    /// </summary>
    private static readonly DateTimeOffset MisfireNow = new DateTimeOffset(2025, 1, 1, 10, 5, 0, TimeSpan.Zero);

    /// <summary>
    /// A simple trigger firing every two minutes from <see cref="MisfireStartTime" />, past due by the
    /// time the frozen <see cref="MisfireNow" /> clock is read.
    /// </summary>
    private static SimpleTriggerImpl CreateMisfiredTrigger(int misfireInstruction, int repeatCount)
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl(new FixedTimeProvider(MisfireNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = MisfireStartTime,
            RepeatInterval = TimeSpan.FromMinutes(2),
            RepeatCount = repeatCount,
            MisfireInstructionCode = misfireInstruction
        };
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc.Should().Be(MisfireStartTime, "the fixture depends on the trigger being past due");
        return trigger;
    }

    [Test]
    public void SmartPolicy_AfterMisfire_IsFireNowForAOneShotTrigger()
    {
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SmartPolicy, repeatCount: 0);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow,
            "SmartPolicy on a trigger that never repeats resolves to FireNow, which is simply 'catch up once'");
        trigger.StartTimeUtc.Should().Be(MisfireStartTime, "FireNow is the one reschedule-now policy that does not re-anchor the start time");
    }

    [Test]
    public void SmartPolicy_AfterMisfire_IsRescheduleNextWithRemainingCountForAnIndefiniteTrigger()
    {
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SmartPolicy, SimpleTriggerImpl.RepeatIndefinitely);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(new DateTimeOffset(2025, 1, 1, 10, 6, 0, TimeSpan.Zero),
            "SmartPolicy on an endlessly repeating trigger resolves to RescheduleNextWithRemainingCount, which resumes on the grid rather than firing now");
        trigger.StartTimeUtc.Should().Be(MisfireStartTime, "the 'next' policies leave the interval grid where it was");
        trigger.TimesTriggered.Should().Be(3,
            "the three fires between 10:00 and 10:06 were skipped, and the count is advanced as though they had happened");
    }

    [Test]
    public void SmartPolicy_AfterMisfire_IsRescheduleNowWithExistingCountForACountedTrigger()
    {
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SmartPolicy, repeatCount: 6);
        trigger.TimesTriggered = 1;

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow,
            "SmartPolicy on a trigger with a finite repeat count resolves to RescheduleNowWithExistingRepeatCount");
        trigger.StartTimeUtc.Should().Be(MisfireNow, "the interval grid is re-anchored to now, which is why the policy 'forgets' the original start time");
        trigger.RepeatCount.Should().Be(5, "the one fire already made is deducted, and the rest are still owed");
        trigger.TimesTriggered.Should().Be(0, "the count restarts along with the start time");
    }

    [Test]
    public void IgnoreMisfirePolicy_AfterMisfire_LeavesThePastDueFireTimeAlone()
    {
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.IgnoreMisfirePolicy, SimpleTriggerImpl.RepeatIndefinitely);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireStartTime,
            "ignoring misfires means the past-due fire time stays put so the trigger fires its way back up to date; " +
            "UpdateAfterMisfire has no branch for the code, and reaching none of them is what makes it a no-op");
        trigger.StartTimeUtc.Should().Be(MisfireStartTime);
        trigger.TimesTriggered.Should().Be(0);
    }

    [Test]
    public void FireNow_AfterMisfire_FiresNowForAOneShotTrigger()
    {
        // The repeating case is rewritten to RescheduleNowWithRemainingRepeatCount, which
        // TriggerDstMisfireTests covers. This is the one that stays FireNow.
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SimpleTrigger.FireNow, repeatCount: 0);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow, "a one-shot trigger simply fires as soon as it can");
        trigger.StartTimeUtc.Should().Be(MisfireStartTime, "nothing about the schedule is rewritten, because there is no schedule left");
        trigger.RepeatCount.Should().Be(0);
    }

    [Test]
    public void RescheduleNowWithExistingRepeatCount_AfterMisfire_RestartsTheScheduleAtNow()
    {
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount, repeatCount: 10);
        trigger.TimesTriggered = 2;

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow, "the policy fires now and rebuilds the schedule from there");
        trigger.StartTimeUtc.Should().Be(MisfireNow);
        trigger.RepeatCount.Should().Be(8,
            "'existing count' means the fires already made are deducted and the missed ones are not, so all eight remaining fires still happen");
        trigger.TimesTriggered.Should().Be(0);
    }

    [Test]
    public void RescheduleNowWithRemainingRepeatCount_AfterMisfire_DropsTheMissedFiresFromTheCount()
    {
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount, repeatCount: 10);
        trigger.TimesTriggered = 1;

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().Be(MisfireNow);
        trigger.StartTimeUtc.Should().Be(MisfireNow);
        trigger.RepeatCount.Should().Be(7,
            "one fire was made and two more were missed between 10:00 and 10:05, and 'remaining count' writes both off - " +
            "this is what separates the policy from RescheduleNowWithExistingRepeatCount");
        trigger.TimesTriggered.Should().Be(0);
    }

    [Test]
    public void RescheduleNowWithRemainingRepeatCount_AfterMisfire_CompletesTheTriggerPastItsEndTime()
    {
        SimpleTriggerImpl trigger = CreateMisfiredTrigger(MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount, repeatCount: 10);
        trigger.EndTimeUtc = new DateTimeOffset(2025, 1, 1, 10, 3, 0, TimeSpan.Zero);

        trigger.UpdateAfterMisfire(null);

        trigger.NextFireTimeUtc.Should().BeNull("the end time passed before misfire handling ran, so there is nothing to reschedule");
        trigger.StartTimeUtc.Should().Be(MisfireStartTime, "the start time is only re-anchored on the branch that actually reschedules");
        trigger.RepeatCount.Should().Be(8,
            "the repeat count is recomputed before the end time is consulted, so a trigger that will never fire again still has its count rewritten - " +
            "harmless because the null fire time completes it, but it means RepeatCount is not a safe record of the original schedule");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        /// <summary>
        /// Static value used when this provider is named by a configuration key and built through its
        /// parameterless constructor.
        /// </summary>
        internal static DateTimeOffset UtcNowValue;

        public FixedTimeProvider() : this(UtcNowValue) { }

        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}