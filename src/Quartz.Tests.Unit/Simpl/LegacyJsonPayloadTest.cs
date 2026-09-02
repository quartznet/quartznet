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

using System.Globalization;
using System.Text;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// Both JSON serializers must keep reading the payload shapes written before 4.0 - those payloads
/// are sitting in users' job store blobs, and an upgrade is not allowed to make them unreadable.
/// </summary>
/// <remarks>
/// <para>
/// The literals here are the shapes an <em>older</em> 3.x wrote: timestamps with no zone marker, a
/// job data map holding a null, triggers from before a trigger could be pinned to a node, and the
/// blank-text shape the dashboard wrote before #3294. Every one of them differs from what a 3.20
/// writes, which is why each is still here rather than superseded.
/// </para>
/// <para>
/// What 3.20 itself writes is pinned by <see cref="Legacy320PayloadTest" /> instead, against files
/// dumped out of the blob columns of a database the released 3.20.0 package filled. Three literals
/// that used to sit here - a monthly, a weekly and a cron calendar - were byte-shape-identical to
/// that capture and have gone, because a hand-transcribed copy of something the suite now has from
/// the source is a second thing to keep in step and no extra evidence.
/// </para>
/// <para>
/// Only reading is covered. Writing the new shape is deliberate, and the round trip through the
/// current shape is covered by <see cref="JsonObjectSerializerTest" />.
/// </para>
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

    /// <summary>
    /// What the dashboard's reschedule wrote before #3294 was fixed: the detail page collapsed a
    /// JSON null to an empty string, so text the trigger did not have arrived as "" rather than null.
    /// </summary>
    private const string BlankTextCronTrigger =
        """
        {
          "TriggerType": "CronTrigger",
          "Key": {
            "Name": "BlankTextTriggerKey",
            "Group": "BlankTextTriggerGroup"
          },
          "JobKey": null,
          "Description": "",
          "CalendarName": "",
          "JobDataMap": {},
          "MisfireInstruction": 0,
          "StartTimeUtc": "2024-07-01T00:00:00+00:00",
          "EndTimeUtc": null,
          "Priority": 5,
          "NextFireTimeUtc": "2024-07-01T03:35:00+00:00",
          "PreviousFireTimeUtc": null,
          "ExecutionGroup": "",
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

    private const string LegacyRecurrenceTrigger =
        """
        {
          "TriggerType": "RecurrenceTrigger",
          "Key": {
            "Name": "RecurrenceTriggerKey",
            "Group": "RecurrenceTriggerGroup"
          },
          "JobKey": null,
          "Description": "RecurrenceTrigger description",
          "CalendarName": null,
          "JobDataMap": {},
          "MisfireInstruction": 2,
          "StartTimeUtc": "2024-07-01T00:00:00+00:00",
          "EndTimeUtc": null,
          "Priority": 5,
          "NextFireTimeUtc": "2024-07-15T09:00:00+09:00",
          "PreviousFireTimeUtc": "2024-07-01T09:00:00+09:00",
          "RecurrenceRule": "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR",
          "TimeZone": "Tokyo Standard Time",
          "TimesTriggered": 6
        }
        """;

    [Test]
    public void AnnualCalendarReadsTheTimestampArrayItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<AnnualCalendar>(LegacyAnnualCalendar);

        calendar.Description.Should().Be("Test AnnualCalendar");
        calendar.CalendarBase.Should().BeOfType<BaseCalendar>();
        calendar.DaysExcluded.Should().BeEquivalentTo([new MonthDay(7, 1), new MonthDay(12, 25)]);
        calendar.IsDayExcluded(new MonthDay(7, 1)).Should().BeTrue("the legacy timestamps collapse to the month and the day");
    }

    [Test]
    public void HolidayCalendarReadsTheTimestampArrayItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<HolidayCalendar>(LegacyHolidayCalendar);

        calendar.Description.Should().Be("Test HolidayCalendar");
        calendar.DaysExcluded.Should().BeEquivalentTo([new DateOnly(2024, 7, 1), new DateOnly(2024, 12, 25)]);
    }

    [Test]
    public void DailyCalendarReadsTheRangeStringsItUsedToBeWrittenWith()
    {
        var calendar = Deserialize<DailyCalendar>(LegacyDailyCalendar);

        calendar.InvertTimeRange.Should().BeTrue();
        calendar.TimeRange.Should().Be(new TimeRange(new TimeOnly(1, 1, 1, 1), new TimeOnly(2, 2, 2, 2)));
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

        // This payload predates retry policies and says nothing about them, which is what every stored
        // trigger written before 4.0 looks like: no policy, and an occurrence that has retried nothing.
        simple.RetryPolicy.Should().BeNull();
        simple.RetryAttempt.Should().Be(0);
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
        cron.RetryPolicy.Should().BeNull();
        cron.RetryAttempt.Should().Be(0);
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3294")]
    public void BlankCalendarNameReadsBackAsNoCalendarAtAll()
    {
        var trigger = Deserialize<IOperableTrigger>(BlankTextCronTrigger);

        trigger.CalendarName.Should().BeNull(
            "every job store gates its calendar lookup on a non-null name, so a blank one would be looked up, not found, and the trigger would never fire again");
        ((TriggerBase) trigger).ExecutionGroup.Should().BeNull("the execution group setter has always normalized blanks");
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

    /// <summary>
    /// The RRULE trigger has been written with these field names since 3.x added it, so its blobs are
    /// as much in the wild as the other four's.
    /// </summary>
    [Test]
    public void RecurrenceTriggerStillReadsItsUnchangedPayload()
    {
        var trigger = Deserialize<IOperableTrigger>(LegacyRecurrenceTrigger);

        var recurrence = trigger.Should().BeOfType<RecurrenceTriggerImpl>().Subject;
        recurrence.Key.Should().Be(new TriggerKey("RecurrenceTriggerKey", "RecurrenceTriggerGroup"));
        recurrence.RecurrenceRule.Should().Be("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR");
        recurrence.TimeZone.Should().Be(TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"),
            "the rule is evaluated in the stored zone, so reading it back as the machine's local zone would silently reschedule the job");
        recurrence.TimesTriggered.Should().Be(6);
        recurrence.MisfireInstructionCode.Should().Be(MisfireInstruction.RecurrenceTrigger.DoNothing);
        recurrence.NextFireTimeUtc.Should().Be(new DateTimeOffset(2024, 7, 15, 9, 0, 0, TimeSpan.FromHours(9)));
    }

    [Test]
    public void TriggerPayloadsWrittenBeforePinningReadBackUnpinned()
    {
        string[] payloads =
        [
            LegacySimpleTrigger,
            LegacyCronTrigger,
            LegacyCalendarIntervalTrigger,
            LegacyDailyTimeIntervalTrigger,
            LegacyRecurrenceTrigger
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

/// <summary>
/// The same promise as <see cref="LegacyJsonPayloadTest" />, against bytes rather than against
/// literals: every payload here was dumped out of a blob column of a database the released
/// <c>Quartz</c> 3.20.0 package filled.
/// </summary>
/// <remarks>
/// <para>
/// <c>src/Quartz.Tests.Unit/TestData/Legacy/3.20/README.md</c> says where they came from and how to
/// regenerate them; <c>src/Quartz.Tests.Integration.Seeder</c> is what produces them, and
/// <c>UpgradeRehearsalTest</c> is the other half of the same evidence — this fixture asks whether 4.0
/// can read what 3.20 wrote, and that one asks whether it can run it.
/// </para>
/// <para>
/// The two folders are read by the serializer that wrote each, because they are not the same payload
/// twice: with the settings a 3.x deployment got by default, Newtonsoft wrote a trigger as a plain
/// object graph carrying <c>$type</c> and System.Text.Json wrote the discriminated form. That default
/// is why <see cref="NewtonsoftJsonObjectSerializer.RegisterTriggerConverters" /> is left alone here
/// and set in <see cref="LegacyJsonPayloadTest" />.
/// </para>
/// <para>
/// Nothing asserts on <c>StartTimeUtc</c> or <c>NextFireTimeUtc</c>: those are the capture's own clock,
/// and regenerating the fixtures moves them.
/// </para>
/// </remarks>
[TestFixture("stj")]
[TestFixture("newtonsoft")]
public class Legacy320PayloadTest
{
    private readonly string folder;
    private readonly IObjectSerializer serializer;

    public Legacy320PayloadTest(string folder)
    {
        this.folder = folder;

        // Each folder is read by the serializer that wrote it, with the settings 3.20 wrote it under.
        serializer = folder == "stj"
            ? new SystemTextJsonObjectSerializer()
            : new NewtonsoftJsonObjectSerializer();
    }

    [Test]
    public void TheJobDataMapReadsBackValueForValue()
    {
        JobDataMap map = Read<JobDataMap>("job-data-map.json");

        map.GetString("text").Should().Be("staging");
        map.GetBoolean("flag").Should().BeTrue();
        map.GetInt("count").Should().Be(42);
        map.GetLong("big").Should().Be(9_000_000_000L);
        map.GetDouble("ratio").Should().Be(2.5);
        map.GetFloat("small").Should().Be(1.5f);
        map.Get<decimal>("money").Should().Be(12.34m);
        map.Get<char>("letter").Should().Be('q');
        map.Get<DateTime>("moment").Should().Be(new DateTime(2024, 7, 1, 3, 30, 0, DateTimeKind.Utc));
        map.GetDateTimeOffset("offsetMoment").Should().Be(new DateTimeOffset(2024, 7, 1, 3, 30, 0, TimeSpan.Zero));
        map.Get<TimeSpan>("span").Should().Be(TimeSpan.FromSeconds(42));
        map.Get<Guid>("id").Should().Be(new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"));
        map.Get<DateOnly>("day").Should().Be(new DateOnly(2024, 7, 1));
        map.Get<TimeOnly>("timeOfDay").Should().Be(new TimeOnly(3, 30, 0));
        map.Get<DayOfWeek>("weekday").Should().Be(DayOfWeek.Friday);

        map.Dirty.Should().BeFalse("a map loaded from the store has not been modified since it was written");
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/3582")]
    public void AStringDictionaryReadsBackAsOneWhicheverWriterDecoratedIt()
    {
        JobDataMap map = Read<JobDataMap>("job-data-map.json");

        map.Get<Dictionary<string, string>>("labels").Should().BeEquivalentTo(
            new Dictionary<string, string> { ["alpha"] = "1", ["beta"] = "2" },
            "3.x's Newtonsoft writer put a $type marker on this value and 4.0's does not, so reading it back "
            + "has to mean reading past the marker rather than handing it over as an entry of its own");
    }

    [Test]
    public void ATriggersOwnJobDataMapReadsBack()
    {
        JobDataMap map = Read<JobDataMap>("trigger-job-data-map.json");

        map.GetString("triggerMarker").Should().Be("on the trigger");
        map.GetInt("triggerNumber").Should().Be(7);
    }

    [TestCase("annual", typeof(AnnualCalendar), "Seeded AnnualCalendar")]
    [TestCase("holiday", typeof(HolidayCalendar), "Seeded HolidayCalendar")]
    [TestCase("monthly", typeof(MonthlyCalendar), "Seeded MonthlyCalendar")]
    [TestCase("weekly", typeof(WeeklyCalendar), "Seeded WeeklyCalendar")]
    [TestCase("daily", typeof(DailyCalendar), "Seeded DailyCalendar")]
    [TestCase("cron", typeof(CronCalendar), "Seeded CronCalendar")]
    public void ACalendarReadsBackAsItsOwnKind(string name, Type expected, string description)
    {
        ICalendar calendar = Read<ICalendar>($"calendar-{name}.json");

        calendar.Should().BeOfType(expected);
        calendar.Description.Should().Be(description);
    }

    /// <summary>
    /// A calendar is only carried across if it still answers the same question the same way, so the
    /// exclusions each kind stores are checked rather than only its type.
    /// </summary>
    [TestCase("annual", "2024-07-01T12:00:00Z", false)]
    [TestCase("annual", "2024-08-05T12:00:00Z", true)]
    [TestCase("holiday", "2024-07-01T12:00:00Z", false)]
    [TestCase("holiday", "2024-07-02T12:00:00Z", true)]
    [TestCase("monthly", "2024-03-10T12:00:00Z", false)]
    [TestCase("monthly", "2024-03-11T12:00:00Z", true)]
    [TestCase("weekly", "2024-07-03T12:00:00Z", false)]
    [TestCase("weekly", "2024-07-04T12:00:00Z", true)]
    [TestCase("daily", "2024-07-01T01:30:00Z", false)]
    [TestCase("daily", "2024-07-01T03:00:00Z", true)]
    [TestCase("cron", "2024-07-01T12:00:00Z", false)]
    [TestCase("cron", "2024-07-01T12:00:01Z", true)]
    [TestCase("chained", "2024-07-01T12:00:01Z", false)]
    [TestCase("chained", "2025-07-02T12:00:03Z", true)]
    public void ACalendarStillAnswersWhatItAnsweredOn320(string name, string instant, bool included)
    {
        ICalendar calendar = Read<ICalendar>($"calendar-{name}.json");

        calendar.IsTimeIncluded(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .Should().Be(included, $"3.20 answered {included} for that instant");
    }

    [Test]
    public void AChainedCalendarBringsItsBaseWithIt()
    {
        ICalendar calendar = Read<ICalendar>("calendar-chained.json");

        calendar.Should().BeOfType<HolidayCalendar>();
        calendar.CalendarBase.Should().BeOfType<CronCalendar>(
            "a chained pair is one blob holding both halves, so a reader that dropped the base would "
            + "silently stop excluding everything the base excluded");
    }

    [Test]
    public void ASimpleTriggerReadsOutOfTheBlobColumn()
    {
        SimpleTriggerImpl trigger = Read<IOperableTrigger>("trigger-simple.json").Should().BeOfType<SimpleTriggerImpl>().Subject;

        trigger.Key.Should().Be(new TriggerKey("simple", "blob"));
        trigger.JobKey.Should().Be(new JobKey("worker", "seed"));
        trigger.CalendarName.Should().Be("holiday");
        trigger.Priority.Should().Be(3);
        trigger.RepeatCount.Should().Be(-1);
        trigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(1));
        trigger.JobDataMap.GetString("triggerMarker").Should().Be("on the trigger");
    }

    [Test]
    public void ACronTriggerReadsOutOfTheBlobColumn()
    {
        CronTriggerImpl trigger = Read<IOperableTrigger>("trigger-cron.json").Should().BeOfType<CronTriggerImpl>().Subject;

        trigger.Key.Should().Be(new TriggerKey("cron", "blob"));
        trigger.CronExpressionString.Should().Be("0/1 * * * * ?");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
        trigger.Priority.Should().Be(7);
    }

    [Test]
    public void ACalendarIntervalTriggerReadsOutOfTheBlobColumn()
    {
        CalendarIntervalTriggerImpl trigger = Read<IOperableTrigger>("trigger-calendar-interval.json")
            .Should().BeOfType<CalendarIntervalTriggerImpl>().Subject;

        trigger.Key.Should().Be(new TriggerKey("calendar-interval", "blob"));
        trigger.RepeatInterval.Should().Be(1);
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Second);
        trigger.PreserveHourOfDayAcrossDaylightSavings.Should().BeTrue();
        trigger.SkipDayIfHourDoesNotExist.Should().BeFalse();
    }

    [Test]
    public void ARecurrenceTriggerReadsOutOfTheBlobColumn()
    {
        RecurrenceTriggerImpl trigger = Read<IOperableTrigger>("trigger-recurrence.json")
            .Should().BeOfType<RecurrenceTriggerImpl>().Subject;

        trigger.Key.Should().Be(new TriggerKey("recurrence", "blob"));
        trigger.RecurrenceRule.Should().Be("FREQ=SECONDLY;INTERVAL=1");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    /// <summary>
    /// The fifth family, on the one serializer that can hold it.
    /// </summary>
    /// <remarks>
    /// There is no Newtonsoft fixture for it, and there cannot be: with the settings a 3.x deployment
    /// got by default, 3.20 writes a daily-time-interval trigger's <c>StartTimeOfDay</c> and
    /// <c>EndTimeOfDay</c> as <c>Quartz.TimeOfDay</c> objects, and <c>TimeOfDay</c> has neither a
    /// parameterless constructor nor a <c>[JsonConstructor]</c> — so <em>3.20 itself</em> throws
    /// reading that blob back. No such row ever worked, on either version.
    /// </remarks>
    [Test]
    public void ADailyTimeIntervalTriggerReadsOutOfTheBlobColumnOnTheSerializerThatCanWriteOne()
    {
        if (folder == "newtonsoft")
        {
            File.Exists(Path.Combine(Directory(), "trigger-daily-time-interval.json")).Should().BeFalse(
                "3.20's default Newtonsoft settings write a blob for this family that 3.20 cannot read back, "
                + "so capturing one would pin a shape no deployment can have had working");
            return;
        }

        DailyTimeIntervalTriggerImpl trigger = Read<IOperableTrigger>("trigger-daily-time-interval.json")
            .Should().BeOfType<DailyTimeIntervalTriggerImpl>().Subject;

        trigger.Key.Should().Be(new TriggerKey("daily-time-interval", "blob"));
        trigger.RepeatInterval.Should().Be(1);
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Second);
        trigger.StartTimeOfDay.Should().Be(new TimeOnly(0, 0, 0));
        trigger.EndTimeOfDay.Should().Be(new TimeOnly(23, 59, 59));
        trigger.DaysOfWeek.Should().HaveCount(7);
    }

    /// <summary>
    /// Every fixture the capture produces is read by something above.
    /// </summary>
    /// <remarks>
    /// Without this, adding a payload to the seeder's dump and forgetting to assert anything about it
    /// would leave a file in the tree that looks like evidence and is not.
    /// </remarks>
    [Test]
    public void EveryCapturedFixtureIsRead()
    {
        string[] captured = System.IO.Directory.GetFiles(Directory(), "*.json")
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray()!;

        string[] read = Asserted
            .Where(x => File.Exists(Path.Combine(Directory(), x)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        captured.Should().BeEquivalentTo(read,
            "a captured blob nothing reads is a file that looks like evidence without being any");
    }

    private static readonly string[] Asserted =
    [
        "job-data-map.json", "trigger-job-data-map.json",
        "calendar-annual.json", "calendar-holiday.json", "calendar-monthly.json", "calendar-weekly.json",
        "calendar-daily.json", "calendar-cron.json", "calendar-chained.json",
        "trigger-simple.json", "trigger-cron.json", "trigger-calendar-interval.json",
        "trigger-daily-time-interval.json", "trigger-recurrence.json"
    ];

    private string Directory() => Path.Combine(AppContext.BaseDirectory, "TestData", "Legacy", "3.20", folder);

    private T Read<T>(string fileName) where T : class
    {
        string path = Path.Combine(Directory(), fileName);
        File.Exists(path).Should().BeTrue($"{fileName} is captured from a 3.20 run and copied to the test output");

        return serializer.Deserialize<T>(File.ReadAllBytes(path))!;
    }
}
