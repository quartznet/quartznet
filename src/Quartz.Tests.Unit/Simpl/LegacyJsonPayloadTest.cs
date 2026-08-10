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

using System.Text;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// Both JSON serializers must keep reading the payload shapes written before 4.0 - those payloads
/// are sitting in users' job store blobs, and an upgrade is not allowed to make them unreadable.
/// The literals below are verbatim output from 3.x.
/// </summary>
/// <remarks>
/// Only reading is covered. Writing the new shape is deliberate, and the round trip through the
/// current shape is covered by <see cref="JsonObjectSerializerTest" />.
/// </remarks>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class LegacyJsonPayloadTest
{
    private readonly IObjectSerializer serializer;

    public LegacyJsonPayloadTest(Type serializerType)
    {
        serializer = (IObjectSerializer) Activator.CreateInstance(serializerType)!;

        if (serializer is NewtonsoftJsonObjectSerializer newtonsoft)
        {
            // Newtonsoft only understands the trigger wire format when its converter is registered.
            newtonsoft.RegisterTriggerConverters = true;
        }
    }

    private const string LegacyAnnualCalendar =
        """
        {
          "$type": "Quartz.Impl.Calendar.AnnualCalendar, Quartz",
          "Description": "Test AnnualCalendar",
          "TimeZoneId": "UTC",
          "BaseCalendar": {
            "$type": "Quartz.Impl.Calendar.BaseCalendar, Quartz",
            "Description": null,
            "TimeZoneId": "UTC",
            "BaseCalendar": null
          },
          "ExcludedDays": [
            "2000-07-01T00:00:00",
            "2000-12-25T00:00:00"
          ]
        }
        """;

    private const string LegacyHolidayCalendar =
        """
        {
          "$type": "Quartz.Impl.Calendar.HolidayCalendar, Quartz",
          "Description": "Test HolidayCalendar",
          "TimeZoneId": "UTC",
          "BaseCalendar": null,
          "ExcludedDates": [
            "2024-07-01T00:00:00",
            "2024-12-25T00:00:00"
          ]
        }
        """;

    private const string LegacyMonthlyCalendar =
        """
        {
          "$type": "Quartz.Impl.Calendar.MonthlyCalendar, Quartz",
          "Description": "Test MonthlyCalendar",
          "TimeZoneId": "UTC",
          "BaseCalendar": null,
          "ExcludedDays": [
            false, false, false, false, false, false, false, false, false, true,
            false, false, false, false, false, false, false, false, false, true,
            false, false, true, false, false, false, false, false, false, true,
            false
          ]
        }
        """;

    private const string LegacyWeeklyCalendar =
        """
        {
          "$type": "Quartz.Impl.Calendar.WeeklyCalendar, Quartz",
          "Description": "Test WeeklyCalendar",
          "TimeZoneId": "UTC",
          "BaseCalendar": null,
          "ExcludedDays": [
            true,
            false,
            false,
            true,
            true,
            true,
            true
          ]
        }
        """;

    private const string LegacyDailyCalendar =
        """
        {
          "$type": "Quartz.Impl.Calendar.DailyCalendar, Quartz",
          "Description": null,
          "TimeZoneId": "UTC",
          "BaseCalendar": {
            "$type": "Quartz.Impl.Calendar.BaseCalendar, Quartz",
            "Description": null,
            "TimeZoneId": "UTC",
            "BaseCalendar": null
          },
          "InvertTimeRange": true,
          "RangeStartingTime": "01:01:01:001",
          "RangeEndingTime": "02:02:02:002"
        }
        """;

    private const string LegacyCronCalendar =
        """
        {
          "$type": "Quartz.Impl.Calendar.CronCalendar, Quartz",
          "Description": "Test CronCalendar",
          "TimeZoneId": "UTC",
          "BaseCalendar": null,
          "CronExpressionString": "0/5 * * * * ?"
        }
        """;

    private const string LegacyJobDataMap =
        """
        {
          "environment": "staging",
          "retryCount": 3,
          "threshold": 2.5,
          "enabled": true,
          "lastRunNote": null
        }
        """;

    private const string LegacySimpleTrigger =
        """
        {
          "TriggerType": "SimpleTrigger",
          "Key": {
            "Name": "SimpleTriggerKey",
            "Group": "SimpleTriggerGroup"
          },
          "JobKey": {
            "Name": "SimpleJob",
            "Group": "SimpleJobGroup"
          },
          "Description": "SimpleTrigger description",
          "CalendarName": "HolidayCalendar",
          "JobDataMap": {
            "environment": "staging",
            "retryCount": 3,
            "enabled": true
          },
          "MisfireInstruction": 1,
          "StartTimeUtc": "2024-07-01T00:00:00.5+00:00",
          "EndTimeUtc": "2024-07-02T00:00:01+00:00",
          "Priority": 5,
          "NextFireTimeUtc": "2024-07-01T03:32:06+00:00",
          "PreviousFireTimeUtc": "2024-07-01T03:31:24+00:00",
          "RepeatCount": 10,
          "RepeatIntervalTimeSpan": "00:00:42",
          "TimesTriggered": 3
        }
        """;

    private const string LegacyCronTrigger =
        """
        {
          "TriggerType": "CronTrigger",
          "Key": {
            "Name": "CronTriggerKey",
            "Group": "CronTriggerGroup"
          },
          "JobKey": null,
          "Description": "CronTrigger description",
          "CalendarName": null,
          "JobDataMap": {},
          "MisfireInstruction": 2,
          "StartTimeUtc": "2024-07-01T00:00:00+00:00",
          "EndTimeUtc": null,
          "Priority": 7,
          "NextFireTimeUtc": "2024-07-01T03:35:00+00:00",
          "PreviousFireTimeUtc": null,
          "CronExpressionString": "0 0/5 * * * ?",
          "TimeZone": "UTC"
        }
        """;

    private const string LegacyCalendarIntervalTrigger =
        """
        {
          "TriggerType": "CalendarIntervalTrigger",
          "Key": {
            "Name": "CalendarIntervalTriggerKey",
            "Group": "CalendarIntervalTriggerGroup"
          },
          "JobKey": null,
          "Description": "CalendarIntervalTrigger description",
          "CalendarName": null,
          "JobDataMap": {},
          "MisfireInstruction": 0,
          "StartTimeUtc": "2024-07-01T00:00:00+00:00",
          "EndTimeUtc": null,
          "Priority": 5,
          "NextFireTimeUtc": "2024-09-01T00:00:00+00:00",
          "PreviousFireTimeUtc": "2024-07-01T00:00:00+00:00",
          "RepeatInterval": 2,
          "RepeatIntervalUnit": "Month",
          "TimeZone": "UTC",
          "PreserveHourOfDayAcrossDaylightSavings": true,
          "SkipDayIfHourDoesNotExist": false,
          "TimesTriggered": 6
        }
        """;

    private const string LegacyDailyTimeIntervalTrigger =
        """
        {
          "TriggerType": "DailyTimeIntervalTrigger",
          "Key": {
            "Name": "DailyTimeIntervalTriggerKey",
            "Group": "DailyTimeIntervalTriggerGroup"
          },
          "JobKey": null,
          "Description": "DailyTimeIntervalTrigger description",
          "CalendarName": null,
          "JobDataMap": {},
          "MisfireInstruction": 0,
          "StartTimeUtc": "2024-07-01T00:00:00.5+00:00",
          "EndTimeUtc": "2024-07-02T00:00:01+00:00",
          "Priority": 5,
          "NextFireTimeUtc": "2024-07-01T03:32:06+00:00",
          "PreviousFireTimeUtc": "2024-07-01T03:31:24+00:00",
          "ExecutionGroup": null,
          "RepeatCount": 1000,
          "RepeatInterval": 42,
          "RepeatIntervalUnit": "Second",
          "StartTimeOfDay": {
            "Hour": 3,
            "Minute": 30,
            "Second": 0
          },
          "EndTimeOfDay": {
            "Hour": 4,
            "Minute": 40,
            "Second": 0
          },
          "DaysOfWeek": [
            "Monday",
            "Wednesday",
            "Friday"
          ],
          "TimeZone": "UTC",
          "TimesTriggered": 4
        }
        """;

    [Test]
    public void AnnualCalendarReadsTheTimestampArrayItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<AnnualCalendar>(LegacyAnnualCalendar);

        calendar.Description.Should().Be("Test AnnualCalendar");
        calendar.CalendarBase.Should().BeOfType<BaseCalendar>();
        calendar.DaysExcluded.Should().BeEquivalentTo([new DateOnly(2000, 7, 1), new DateOnly(2000, 12, 25)]);
        calendar.IsDayExcluded(new DateOnly(2031, 7, 1)).Should().BeTrue("only the month and the day are significant");
    }

    [Test]
    public void HolidayCalendarReadsTheTimestampArrayItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<HolidayCalendar>(LegacyHolidayCalendar);

        calendar.Description.Should().Be("Test HolidayCalendar");
        calendar.DaysExcluded.Should().BeEquivalentTo([new DateOnly(2024, 7, 1), new DateOnly(2024, 12, 25)]);
    }

    [Test]
    public void MonthlyCalendarReadsTheFlagPerDayArrayItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<MonthlyCalendar>(LegacyMonthlyCalendar);

        calendar.Description.Should().Be("Test MonthlyCalendar");
        calendar.DaysExcluded.Should().BeEquivalentTo([10, 20, 23, 30]);
    }

    [Test]
    public void WeeklyCalendarReadsTheFlagPerDayArrayItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<WeeklyCalendar>(LegacyWeeklyCalendar);

        calendar.Description.Should().Be("Test WeeklyCalendar");
        calendar.DaysExcluded.Should().BeEquivalentTo(
            [DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
            "the payload is the whole truth, so the default weekend exclusion must not leak through");
    }

    [Test]
    public void DailyCalendarReadsTheRangeStringsItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<DailyCalendar>(LegacyDailyCalendar);

        calendar.InvertTimeRange.Should().BeTrue();
        calendar.TimeRange.Should().Be((new TimeOnly(1, 1, 1, 1), new TimeOnly(2, 2, 2, 2)));
    }

    [Test]
    public void CronCalendarStillReadsItsUnchangedPayload()
    {
        var calendar = Deserialize<CronCalendar>(LegacyCronCalendar);

        calendar.CronExpression.CronExpressionString.Should().Be("0/5 * * * * ?");
    }

    [Test]
    public void JobDataMapStillReadsTheBlobTheJobDataColumnHolds()
    {
        var map = Deserialize<JobDataMap>(LegacyJobDataMap);

        map.GetString("environment").Should().Be("staging");
        map.GetInt("retryCount").Should().Be(3);
        map.GetDouble("threshold").Should().Be(2.5);
        map.GetBoolean("enabled").Should().BeTrue();
        map["lastRunNote"].Should().BeNull();
        map.Dirty.Should().BeFalse("a map loaded from the store has not been modified since it was written");
    }

    [Test]
    public void SimpleTriggerStillReadsItsUnchangedPayload()
    {
        var trigger = Deserialize<IOperableTrigger>(LegacySimpleTrigger);

        var simple = trigger.Should().BeOfType<SimpleTriggerImpl>().Subject;
        simple.Key.Should().Be(new TriggerKey("SimpleTriggerKey", "SimpleTriggerGroup"));
        simple.JobKey.Should().Be(new JobKey("SimpleJob", "SimpleJobGroup"));
        simple.CalendarName.Should().Be("HolidayCalendar");
        simple.RepeatCount.Should().Be(10);
        simple.RepeatInterval.Should().Be(TimeSpan.FromSeconds(42));
        simple.TimesTriggered.Should().Be(3);
        simple.MisfireInstructionCode.Should().Be(MisfireInstruction.SimpleTrigger.FireNow);
        simple.NextFireTimeUtc.Should().Be(new DateTimeOffset(2024, 7, 1, 3, 32, 6, TimeSpan.Zero));

        simple.JobDataMap.GetString("environment").Should().Be("staging");
        simple.JobDataMap.GetInt("retryCount").Should().Be(3);
        simple.JobDataMap.GetBoolean("enabled").Should().BeTrue();
    }

    [Test]
    public void CronTriggerStillReadsItsUnchangedPayload()
    {
        var trigger = Deserialize<IOperableTrigger>(LegacyCronTrigger);

        var cron = trigger.Should().BeOfType<CronTriggerImpl>().Subject;
        cron.Key.Should().Be(new TriggerKey("CronTriggerKey", "CronTriggerGroup"));
        cron.CronExpressionString.Should().Be("0 0/5 * * * ?");
        cron.TimeZone.Should().Be(TimeZoneInfo.Utc);
        cron.Priority.Should().Be(7);
        cron.MisfireInstructionCode.Should().Be(MisfireInstruction.CronTrigger.DoNothing);
        cron.EndTimeUtc.Should().BeNull();
    }

    [Test]
    public void CalendarIntervalTriggerStillReadsItsUnchangedPayload()
    {
        var trigger = Deserialize<IOperableTrigger>(LegacyCalendarIntervalTrigger);

        var calendarInterval = trigger.Should().BeOfType<CalendarIntervalTriggerImpl>().Subject;
        calendarInterval.RepeatInterval.Should().Be(2);
        calendarInterval.RepeatIntervalUnit.Should().Be(IntervalUnit.Month);
        calendarInterval.PreserveHourOfDayAcrossDaylightSavings.Should().BeTrue();
        calendarInterval.SkipDayIfHourDoesNotExist.Should().BeFalse();
        calendarInterval.TimesTriggered.Should().Be(6);
    }

    [Test]
    public void DailyTimeIntervalTriggerReadsTheHourMinuteSecondObjects()
    {
        var trigger = Deserialize<IOperableTrigger>(LegacyDailyTimeIntervalTrigger);

        var daily = trigger.Should().BeOfType<DailyTimeIntervalTriggerImpl>().Subject;
        daily.StartTimeOfDay.Should().Be(new TimeOnly(3, 30, 0));
        daily.EndTimeOfDay.Should().Be(new TimeOnly(4, 40, 0));
        daily.RepeatCount.Should().Be(1000);
        daily.RepeatInterval.Should().Be(42);
        daily.RepeatIntervalUnit.Should().Be(IntervalUnit.Second);
        daily.DaysOfWeek.Should().BeEquivalentTo([DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);
        daily.TimesTriggered.Should().Be(4);
    }

    [Test]
    public void TriggerPayloadsWrittenBeforePinningReadBackUnpinned()
    {
        string[] payloads =
        [
            LegacySimpleTrigger,
            LegacyCronTrigger,
            LegacyCalendarIntervalTrigger,
            LegacyDailyTimeIntervalTrigger
        ];

        foreach (string payload in payloads)
        {
            IOperableTrigger trigger = Deserialize<IOperableTrigger>(payload);

            trigger.PreferredNode.Should().Be(
                PreferredNode.None,
                "a payload written before triggers could be pinned carries neither half of the pin, and a reader must not invent one");
        }
    }

    private T Deserialize<T>(string json) where T : class
    {
        return serializer.Deserialize<T>(Encoding.UTF8.GetBytes(json))!;
    }
}
