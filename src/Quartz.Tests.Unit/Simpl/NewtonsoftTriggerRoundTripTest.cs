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

using System.Text;
using System.Text.Json;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// The Newtonsoft serializer writes a trigger as a plain object graph unless
/// <see cref="NewtonsoftJsonObjectSerializer.RegisterTriggerConverters" /> is on, and reads it back
/// by constructing the concrete type reflectively. That path needs a genuinely parameterless
/// constructor on every trigger implementation - a constructor whose only parameter has a default
/// value does not count as one - so each of the five gets a round trip here.
/// </summary>
/// <remarks>
/// Both settings are exercised, because both are shapes a stored trigger comes back through and the
/// two agree on nothing automatically: with the converters on a trigger is rebuilt from a schedule
/// builder, and with them off it is a property-by-property read of the object graph. The zone is
/// asserted on every trigger that carries one, which is what #3494 was: written as an object graph of
/// read-only properties, a zone read back as nothing at all and the trigger silently adopted the
/// reading machine's.
/// </remarks>
[TestFixture(false)]
[TestFixture(true)]
public class NewtonsoftTriggerRoundTripTest
{
    private static readonly DateTimeOffset startTime = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A zone this machine is not in, so that a trigger whose zone was dropped comes back visibly
    /// wrong rather than accidentally right. Tokyo unless the test is running there, which is the
    /// only case the fallback exists for.
    /// </summary>
    internal static readonly TimeZoneInfo NonLocalZone = PickNonLocalZone();

    private readonly bool registerTriggerConverters;

    private NewtonsoftJsonObjectSerializer serializer;

    public NewtonsoftTriggerRoundTripTest(bool registerTriggerConverters)
    {
        this.registerTriggerConverters = registerTriggerConverters;
    }

    [SetUp]
    public void SetUp()
    {
        serializer = new NewtonsoftJsonObjectSerializer { RegisterTriggerConverters = registerTriggerConverters };
    }

    private static TimeZoneInfo PickNonLocalZone()
    {
        TimeZoneInfo tokyo = TimeZones.FindById("Tokyo Standard Time");
        return tokyo.Equals(TimeZoneInfo.Local) ? TimeZones.FindById("Eastern Standard Time") : tokyo;
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
            TimeZone = NonLocalZone,
            StartTimeUtc = startTime,
            EndTimeUtc = startTime.AddDays(1)
        };

        CronTriggerImpl restored = RoundTrip(trigger);

        restored.CronExpressionString.Should().Be("0/5 * * * * ?");
        restored.TimeZone.Should().Be(NonLocalZone);
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
            TimeZone = NonLocalZone,
            StartTimeUtc = startTime
        };

        CalendarIntervalTriggerImpl restored = RoundTrip(trigger);

        restored.RepeatInterval.Should().Be(3);
        restored.RepeatIntervalUnit.Should().Be(IntervalUnit.Hour);
        restored.TimeZone.Should().Be(NonLocalZone,
            "a day or month interval is counted in the stored zone, so reading it back as the machine's own zone reschedules the job");
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
            TimeZone = NonLocalZone,
            StartTimeUtc = startTime
        };

        DailyTimeIntervalTriggerImpl restored = RoundTrip(trigger);

        restored.RepeatInterval.Should().Be(42);
        restored.RepeatIntervalUnit.Should().Be(IntervalUnit.Second);
        restored.StartTimeOfDay.Should().Be(new TimeOnly(3, 30));
        restored.EndTimeOfDay.Should().Be(new TimeOnly(4, 40));
        restored.DaysOfWeek.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday });
        restored.TimeZone.Should().Be(NonLocalZone,
            "the daily window is wall-clock time in the stored zone, so a lost zone moves every firing");
    }

    [Test]
    public void RecurrenceTriggerSurvivesTheRoundTrip()
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl
        {
            Key = new TriggerKey("recurrence", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            RecurrenceRule = "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR",
            TimesTriggered = 3,
            TimeZone = NonLocalZone,
            StartTimeUtc = startTime,
            EndTimeUtc = startTime.AddDays(30)
        };

        RecurrenceTriggerImpl restored = RoundTrip(trigger);

        restored.RecurrenceRule.Should().Be("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR",
            "the rule is the whole schedule, so a trigger that loses it fires on nothing");
        restored.TimesTriggered.Should().Be(3);
        restored.TimeZone.Should().Be(NonLocalZone,
            "the rule is evaluated in the stored zone, so a lost zone is a silently rewritten schedule");
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

/// <summary>
/// The plain object graph as it was written before #3494 was fixed, with the whole
/// <see cref="TimeZoneInfo" /> spelled out. Blobs in this shape are in job store columns now, so the
/// converter that writes the id from here on has to keep reading them.
/// </summary>
/// <remarks>
/// <para>
/// The literals are verbatim output of the code at 347392bd - <c>NewtonsoftJsonObjectSerializer</c>
/// with its default settings, serializing a trigger whose zone was Tokyo - captured before the
/// converter was written, so "it still reads" is a fact about the bytes rather than a reconstruction
/// of them. The display and standard names are the Windows spellings the capturing machine had; only
/// <c>Id</c> is ever read, which is exactly why the rest of the object was worthless.
/// </para>
/// <para>
/// Converters off throughout, because this shape is only ever written with them off - with them on the
/// payload is the <c>TriggerType</c> form <see cref="LegacyJsonPayloadTest" /> covers.
/// </para>
/// </remarks>
[TestFixture]
public class NewtonsoftLegacyTimeZonePayloadTest
{
    private const string LegacyCalendarIntervalTrigger =
        """{"$type":"Quartz.Impl.Triggers.CalendarIntervalTriggerImpl, Quartz","StartTimeUtc":"2024-07-01T00:00:00+00:00","MisfireInstruction":0,"RepeatIntervalUnit":3,"RepeatInterval":3,"TimeZone":{"Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false},"PreserveHourOfDayAcrossDaylightSavings":false,"SkipDayIfHourDoesNotExist":false,"TimesTriggered":0,"MayFireAgain":false,"Key":{"Name":"calendarInterval","Group":"group"},"JobKey":{"Name":"job","Group":"jobGroup"},"PreferredNode":{"IsAutomatic":false,"IsNone":true},"JobDataMap":{},"MisfireInstructionCode":0,"Priority":5,"HasAdditionalProperties":false}""";

    private const string LegacyDailyTimeIntervalTrigger =
        """{"$type":"Quartz.Impl.Triggers.DailyTimeIntervalTriggerImpl, Quartz","StartTimeUtc":"2024-07-01T00:00:00+00:00","RepeatCount":-1,"RepeatIntervalUnit":1,"RepeatInterval":42,"TimesTriggered":0,"TimeZone":{"Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false},"MayFireAgain":false,"DaysOfWeek":{"$type":"System.Collections.Generic.HashSet`1[[System.DayOfWeek, System.Private.CoreLib]], System.Private.CoreLib","$values":[1,3]},"StartTimeOfDay":"03:30:00","EndTimeOfDay":"04:40:00","MisfireInstruction":0,"Key":{"Name":"dailyTimeInterval","Group":"group"},"JobKey":{"Name":"job","Group":"jobGroup"},"PreferredNode":{"IsAutomatic":false,"IsNone":true},"JobDataMap":{},"MisfireInstructionCode":0,"Priority":5,"HasAdditionalProperties":false}""";

    private const string LegacyRecurrenceTrigger =
        """{"$type":"Quartz.Impl.Triggers.RecurrenceTriggerImpl, Quartz","RecurrenceRule":"FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR","TimeZone":{"Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false},"TimesTriggered":0,"StartTimeUtc":"2024-07-01T00:00:00+00:00","MisfireInstruction":0,"MayFireAgain":false,"Key":{"Name":"recurrence","Group":"group"},"JobKey":{"Name":"job","Group":"jobGroup"},"PreferredNode":{"IsAutomatic":false,"IsNone":true},"JobDataMap":{},"MisfireInstructionCode":0,"Priority":5,"HasAdditionalProperties":false}""";

    private const string LegacyCronTrigger =
        """{"$type":"Quartz.Impl.Triggers.CronTriggerImpl, Quartz","CronExpressionString":"0/5 * * * * ?","CronExpression":{"$type":"Quartz.CronExpression, Quartz","CronExpression":"0/5 * * * * ?","TimeZoneId":"Tokyo Standard Time"},"StartTimeUtc":"2024-07-01T00:00:00+00:00","TimeZone":{"Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false},"MisfireInstruction":0,"MayFireAgain":false,"Key":{"Name":"cron","Group":"group"},"JobKey":{"Name":"job","Group":"jobGroup"},"PreferredNode":{"IsAutomatic":false,"IsNone":true},"JobDataMap":{},"MisfireInstructionCode":0,"Priority":5,"HasAdditionalProperties":false}""";

    private static TimeZoneInfo Tokyo => TimeZones.FindById("Tokyo Standard Time");

    [Test]
    public void CalendarIntervalTriggerReadsTheTimeZoneObjectItUsedToBeWrittenWith()
    {
        CalendarIntervalTriggerImpl trigger = Deserialize<CalendarIntervalTriggerImpl>(LegacyCalendarIntervalTrigger);

        trigger.TimeZone.Should().Be(Tokyo);
        trigger.RepeatInterval.Should().Be(3, "the rest of the payload has to read as it always did");
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Hour);
    }

    [Test]
    public void DailyTimeIntervalTriggerReadsTheTimeZoneObjectItUsedToBeWrittenWith()
    {
        DailyTimeIntervalTriggerImpl trigger = Deserialize<DailyTimeIntervalTriggerImpl>(LegacyDailyTimeIntervalTrigger);

        trigger.TimeZone.Should().Be(Tokyo);
        trigger.StartTimeOfDay.Should().Be(new TimeOnly(3, 30));
        trigger.DaysOfWeek.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday });
    }

    [Test]
    public void RecurrenceTriggerReadsTheTimeZoneObjectItUsedToBeWrittenWith()
    {
        RecurrenceTriggerImpl trigger = Deserialize<RecurrenceTriggerImpl>(LegacyRecurrenceTrigger);

        trigger.TimeZone.Should().Be(Tokyo);
        trigger.RecurrenceRule.Should().Be("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR");
    }

    /// <summary>
    /// The cron trigger was the one that escaped the bug, because its zone rides on the
    /// <see cref="CronExpression" />, which has had a converter all along. It is here so that the
    /// object form beside it - which is now read too - cannot start contradicting the expression.
    /// </summary>
    [Test]
    public void CronTriggerReadsTheTimeZoneObjectAndTheExpressionAgree()
    {
        CronTriggerImpl trigger = Deserialize<CronTriggerImpl>(LegacyCronTrigger);

        trigger.TimeZone.Should().Be(Tokyo);
        trigger.CronExpression!.TimeZone.Should().Be(Tokyo);
        trigger.CronExpressionString.Should().Be("0/5 * * * * ?");
    }

    private static T Deserialize<T>(string json) where T : class
    {
        // The default settings, which is what wrote these payloads.
        IObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();
        return serializer.Deserialize<T>(Encoding.UTF8.GetBytes(json))!;
    }
}

/// <summary>
/// A <see cref="TimeZoneInfo" /> in a job data map is refused when it is written; the same zone on a
/// trigger's <c>TimeZone</c> property travels as its id. The difference is that the converter for a
/// zone is attached to typed members by <c>QuartzContractResolver</c> rather than registered on the
/// serializer, so it never reaches an <see cref="object" />-typed slot — and what did reach one was a
/// payload nothing could read back.
/// </summary>
/// <remarks>
/// <para>
/// Scoping is what keeps the trigger side true: a converter on the serializer's list is consulted for a
/// value's runtime type wherever it appears, so a global one would have written a zone held as job data
/// as a bare string and dropped the <c>$type</c> that the object-typed path carries. That left the job
/// data side written and unreadable, which is the asymmetry #3500 closed — by refusing the value rather
/// than by teaching the map to carry a zone, so that both serializers accept the same things.
/// </para>
/// <para>
/// The trigger's written shape is asserted member by member rather than against a captured string,
/// because most of what Json.NET writes for a zone is the running OS's own text: Windows says
/// <c>"(UTC+09:00) Osaka, Sapporo, Tokyo"</c> where Linux says
/// <c>"(UTC+09:00) Japan Standard Time (Tokyo)"</c>, and the standard and daylight names differ with
/// them. Only <c>$type</c> and <c>Id</c> mean the same thing everywhere — and the id is compared
/// against the zone the test itself resolved rather than a literal, since a Windows id resolves to an
/// IANA one on Unix and the spelling belongs to the platform.
/// </para>
/// <para>
/// <see cref="JobDataMapWithZone" /> is verbatim output of the code at 347392bd and is used for reading
/// only, which is safe: what makes that read fail has nothing to do with the names in it.
/// </para>
/// </remarks>
[TestFixture]
public class NewtonsoftJobDataTimeZoneTest
{
    private const string JobDataMapWithZone =
        """{"zone":{"$type":"System.TimeZoneInfo, System.Private.CoreLib","Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false}}""";

    [Test]
    public void AZoneInAJobDataMapIsRefusedOnWrite()
    {
        IObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();
        TimeZoneInfo tokyo = TimeZones.FindById("Tokyo Standard Time");
        JobDataMap map = new JobDataMap { { "zone", tokyo } };

        Action write = () => serializer.Serialize(map);

        write.Should().Throw<JsonSerializationException>(
                "the payload this used to write cannot be read by anything, so the write is where the story has to end")
            .Which.Message.Should().Contain("zone", "the failure has to say which entry it is about")
            .And.Contain("System.TimeZoneInfo", "and what was in it")
            .And.Contain("AddJobDataValueType", "and how an application declares a type of its own");
    }

    [Test]
    public void ATriggerWritesItsZoneAsTheIdInstead()
    {
        IObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();
        TimeZoneInfo tokyo = TimeZones.FindById("Tokyo Standard Time");
        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl
        {
            Key = new TriggerKey("calendarInterval", "group"),
            RepeatIntervalUnit = IntervalUnit.Hour,
            TimeZone = tokyo,
            StartTimeUtc = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero)
        };

        using JsonDocument written = JsonDocument.Parse(serializer.Serialize(trigger));
        JsonElement zone = written.RootElement.GetProperty("TimeZone");

        zone.ValueKind.Should().Be(JsonValueKind.String,
            "a typed member is where the converter applies, so the whole object collapses to one value");
        zone.GetString().Should().Be(tokyo.Id,
            "the id is the only part of a zone another process can use, and its spelling is the resolving platform's own");
    }

    /// <summary>
    /// With <see cref="NewtonsoftJsonObjectSerializer.RegisterTriggerConverters" /> on, a trigger's own
    /// job data is written by the trigger converter rather than by the map's, and that is a second way
    /// into the same column — so it is gated too, and gated before the object is opened, so a trigger
    /// carrying one unreadable value writes nothing at all.
    /// </summary>
    /// <remarks>
    /// The converter wraps what goes wrong inside it in "Failed to serialize ITrigger to json", which
    /// would bury the one message that says which entry to fix; a refusal is rethrown instead, exactly
    /// as the System.Text.Json job data map converter rethrows its own.
    /// </remarks>
    [Test]
    public void ATriggersOwnJobDataIsRefusedWithTheTriggerConvertersOnToo()
    {
        NewtonsoftJsonObjectSerializer serializer = new() { RegisterTriggerConverters = true };
        CalendarIntervalTriggerImpl trigger = new()
        {
            Key = new TriggerKey("calendarInterval", "group"),
            RepeatIntervalUnit = IntervalUnit.Hour,
            StartTimeUtc = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero)
        };
        trigger.JobDataMap.Add("zone", TimeZones.FindById("Tokyo Standard Time"));

        Action write = () => serializer.Serialize<ITrigger>(trigger);

        write.Should().Throw<JsonSerializationException>(
                "a trigger's job data lands in the same column as a job's, and neither reader can turn a written zone back into one")
            .Which.Message.Should().Contain("zone", "the failure has to say which entry it is about")
            .And.Contain("System.TimeZoneInfo", "and what was in it")
            .And.NotContain("Failed to serialize ITrigger",
                "the trigger converter's own wrapper would hide the only sentence that says what to change");
    }

    /// <summary>
    /// Reading that job data payload back has never worked, and still does not:
    /// <c>IgnoreSerializableInterface</c> keeps Json.NET off <see cref="TimeZoneInfo" />'s
    /// <c>ISerializable</c> implementation, and every public member it writes instead is read-only, so
    /// there is nothing to construct the zone from. That is the whole reason the write above is refused
    /// — and a column that already holds one of these is why the read is left alone rather than taught
    /// to guess, since the failure names the payload and a job that needs a zone should store the id.
    /// </summary>
    [Test]
    public void ReadingThatZoneBackHasNeverWorked()
    {
        IObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();

        Action read = () => serializer.Deserialize<JobDataMap>(Encoding.UTF8.GetBytes(JobDataMapWithZone));

        read.Should().Throw<JsonSerializationException>(
            "a TimeZoneInfo has no constructor Json.NET can call, which is why a job data map should hold the id and not the zone");
    }
}
