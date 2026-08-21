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
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.  See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// The Newtonsoft serializer writes a trigger as a plain object graph unless
/// <see cref="NewtonsoftJsonObjectSerializer.RegisterTriggerConverters" /> is on, and reads it back
/// by constructing the concrete type reflectively. That path needs a genuinely parameterless
/// constructor on every trigger implementation - a constructor whose only parameter has a default
/// value does not count as one - so each of the four gets a round trip here.
/// </summary>
[TestFixture]
public class NewtonsoftTriggerRoundTripTest
{
    private static readonly DateTimeOffset startTime = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private NewtonsoftJsonObjectSerializer serializer;

    [SetUp]
    public void SetUp()
    {
        // Deliberately left at its default: this is the shape that has to keep working, because the
        // converters that would otherwise construct the trigger are not registered.
        serializer = new NewtonsoftJsonObjectSerializer();
    }

    [Test]
    public void SimpleTriggerSurvivesTheRoundTrip()
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl
        {
            Key = new TriggerKey("simple", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            StartTimeUtc = startTime,
            EndTimeUtc = startTime.AddDays(1),
            RepeatCount = 7,
            RepeatInterval = TimeSpan.FromMinutes(3)
        };

        SimpleTriggerImpl restored = RoundTrip(trigger);

        restored.RepeatCount.Should().Be(7);
        restored.RepeatInterval.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Test]
    public void CronTriggerSurvivesTheRoundTrip()
    {
        CronTriggerImpl trigger = new CronTriggerImpl
        {
            Key = new TriggerKey("cron", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            CronExpressionString = "0/5 * * * * ?",
            TimeZone = TimeZoneInfo.Utc,
            StartTimeUtc = startTime,
            EndTimeUtc = startTime.AddDays(1)
        };

        CronTriggerImpl restored = RoundTrip(trigger);

        restored.CronExpressionString.Should().Be("0/5 * * * * ?");
        restored.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Test]
    public void CalendarIntervalTriggerSurvivesTheRoundTrip()
    {
        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl
        {
            Key = new TriggerKey("calendarInterval", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            RepeatInterval = 3,
            RepeatIntervalUnit = IntervalUnit.Hour,
            TimeZone = TimeZoneInfo.Utc,
            StartTimeUtc = startTime
        };

        CalendarIntervalTriggerImpl restored = RoundTrip(trigger);

        restored.RepeatInterval.Should().Be(3);
        restored.RepeatIntervalUnit.Should().Be(IntervalUnit.Hour);
    }

    [Test]
    public void DailyTimeIntervalTriggerSurvivesTheRoundTrip()
    {
        DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl
        {
            Key = new TriggerKey("dailyTimeInterval", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            RepeatInterval = 42,
            RepeatIntervalUnit = IntervalUnit.Second,
            StartTimeOfDay = new TimeOnly(3, 30),
            EndTimeOfDay = new TimeOnly(4, 40),
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Wednesday],
            TimeZone = TimeZoneInfo.Utc,
            StartTimeUtc = startTime
        };

        DailyTimeIntervalTriggerImpl restored = RoundTrip(trigger);

        restored.RepeatInterval.Should().Be(42);
        restored.RepeatIntervalUnit.Should().Be(IntervalUnit.Second);
        restored.StartTimeOfDay.Should().Be(new TimeOnly(3, 30));
        restored.EndTimeOfDay.Should().Be(new TimeOnly(4, 40));
        restored.DaysOfWeek.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday });
    }

    private T RoundTrip<T>(T trigger) where T : class, IOperableTrigger
    {
        byte[] bytes = ((IObjectSerializer) serializer).Serialize(trigger);
        T restored = ((IObjectSerializer) serializer).Deserialize<T>(bytes)!;

        restored.Should().NotBeNull();
        restored.Key.Should().Be(trigger.Key);
        restored.JobKey.Should().Be(trigger.JobKey);
        restored.StartTimeUtc.Should().Be(trigger.StartTimeUtc);
        restored.EndTimeUtc.Should().Be(trigger.EndTimeUtc);
        return restored;
    }
}
