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
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A trigger's time zone has to survive being written and read again, whichever of the two shapes the
/// Newtonsoft serializer is set to write. With <c>RegisterTriggerConverters</c> on - which is not the
/// default - a trigger goes out through its <c>ITriggerSerializer</c>; with it off it is written
/// property by property as a plain object graph, and that is the path #3494 was about: Json.NET's
/// default contract wrote a <see cref="TimeZoneInfo" /> as its whole public surface, every member of
/// which is read-only, so reading it back set nothing and the trigger's getter fell through to
/// <see cref="TimeZoneInfo.Local" />.
/// </summary>
/// <remarks>
/// Every zone here is one the test machine is not in, so a trigger whose zone was dropped comes back
/// visibly wrong rather than accidentally right.
/// </remarks>
public class NewtonsoftTriggerTimeZoneTest
{
    private static readonly DateTimeOffset startTime = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Tokyo unless the test is running there, which is the only case the fallback exists for.
    /// </summary>
    internal static readonly TimeZoneInfo NonLocalZone = PickNonLocalZone();

    private static TimeZoneInfo PickNonLocalZone()
    {
        TimeZoneInfo tokyo = TimeZoneUtil.FindTimeZoneById("Tokyo Standard Time");
        return tokyo.Equals(TimeZoneInfo.Local) ? TimeZoneUtil.FindTimeZoneById("Eastern Standard Time") : tokyo;
    }

    internal static JsonObjectSerializer CreateSerializer(bool registerTriggerConverters)
    {
        JsonObjectSerializer serializer = new JsonObjectSerializer { RegisterTriggerConverters = registerTriggerConverters };
        serializer.Initialize();
        return serializer;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CalendarIntervalTriggerKeepsItsTimeZone(bool registerTriggerConverters)
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

        CalendarIntervalTriggerImpl restored = RoundTrip(trigger, registerTriggerConverters);

        restored.RepeatInterval.Should().Be(3, "the rest of the trigger has to read as it always did");
        restored.RepeatIntervalUnit.Should().Be(IntervalUnit.Hour);
        restored.TimeZone.Should().Be(NonLocalZone,
            "a day or month interval is counted in the stored zone, so reading it back as the machine's own zone reschedules the job");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RecurrenceTriggerKeepsItsTimeZone(bool registerTriggerConverters)
    {
        RecurrenceTriggerImpl trigger = new RecurrenceTriggerImpl
        {
            Key = new TriggerKey("recurrence", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            RecurrenceRule = "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR",
            TimeZone = NonLocalZone,
            StartTimeUtc = startTime
        };

        RecurrenceTriggerImpl restored = RoundTrip(trigger, registerTriggerConverters);

        restored.RecurrenceRule.Should().Be("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR",
            "the rule is the whole schedule, so a trigger that loses it fires on nothing");
        restored.TimeZone.Should().Be(NonLocalZone,
            "the rule is evaluated in the stored zone, so a lost zone is a silently rewritten schedule");
    }

    /// <summary>
    /// The cron trigger escaped this bug on 4.x, because its zone rides along on a serialized
    /// <c>CronExpression</c>, which has always had a converter. Here it does not: 3.x writes only
    /// <c>CronExpressionString</c>, and a bare cron string carries no zone, so before the converter the
    /// zone went the same way every other trigger's did.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void CronTriggerKeepsItsTimeZone(bool registerTriggerConverters)
    {
        CronTriggerImpl trigger = new CronTriggerImpl
        {
            Key = new TriggerKey("cron", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            CronExpressionString = "0/5 * * * * ?",
            TimeZone = NonLocalZone,
            StartTimeUtc = startTime
        };

        CronTriggerImpl restored = RoundTrip(trigger, registerTriggerConverters);

        restored.CronExpressionString.Should().Be("0/5 * * * * ?");
        restored.TimeZone.Should().Be(NonLocalZone,
            "a cron expression is resolved in the stored zone, the string form does not carry one, and this getter reads through to the rebuilt expression - so it is also the assertion that the expression got the zone");
    }

    /// <summary>
    /// The daily trigger's write side is asserted on its own because its read side cannot be reached
    /// with the trigger converters off - see
    /// <see cref="ReadingADailyTimeIntervalTriggerAsAPlainObjectGraphFailsOnItsTimeOfDay" />.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void DailyTimeIntervalTriggerWritesItsTimeZoneAsAnId(bool registerTriggerConverters)
    {
        DailyTimeIntervalTriggerImpl trigger = CreateDailyTimeIntervalTrigger();

        JObject written = Write(trigger, registerTriggerConverters);

        written["TimeZone"].Type.Should().Be(JTokenType.String,
            "the id is the only part of a zone another process can use; the object form the default contract wrote was unreadable");
        written["TimeZone"].Value<string>().Should().Be(NonLocalZone.Id,
            "the spelling of a resolved zone's id belongs to the platform and the tz database, so it is compared against the zone in hand");
    }

    [Test]
    public void DailyTimeIntervalTriggerKeepsItsTimeZone()
    {
        DailyTimeIntervalTriggerImpl trigger = CreateDailyTimeIntervalTrigger();

        DailyTimeIntervalTriggerImpl restored = RoundTrip(trigger, registerTriggerConverters: true);

        restored.StartTimeOfDay.Should().Be(new TimeOfDay(3, 30), "the rest of the trigger has to read as it always did");
        restored.EndTimeOfDay.Should().Be(new TimeOfDay(4, 40));
        restored.DaysOfWeek.Should().BeEquivalentTo(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday });
        restored.TimeZone.Should().Be(NonLocalZone,
            "the daily window is wall-clock time in the stored zone, so a lost zone moves every firing");
    }

    /// <summary>
    /// A defect of the same family as #3494 and deliberately not fixed here: with the trigger
    /// converters off a <see cref="DailyTimeIntervalTriggerImpl" /> cannot be read back at all, because
    /// <see cref="TimeOfDay" /> has two public constructors and no parameterless one, so Json.NET has
    /// nothing to build <c>EndTimeOfDay</c> with. It has nothing to do with the time zone - the zone is
    /// written correctly, as
    /// <see cref="DailyTimeIntervalTriggerWritesItsTimeZoneAsAnId" /> shows - and repairing it needs a
    /// converter of its own, so it is recorded here rather than quietly folded in.
    /// </summary>
    /// <remarks>
    /// This is a loud failure rather than a silent one, which is why it is the lesser bug: nothing
    /// comes back wrong, the read throws. <c>StartTimeOfDay</c> is the silent half of it - its getter
    /// hands out a default <c>00:00:00</c> for Json.NET to populate, and every member of a
    /// <see cref="TimeOfDay" /> is read-only.
    /// </remarks>
    [Test]
    public void ReadingADailyTimeIntervalTriggerAsAPlainObjectGraphFailsOnItsTimeOfDay()
    {
        JsonObjectSerializer serializer = CreateSerializer(registerTriggerConverters: false);
        byte[] bytes = serializer.Serialize(CreateDailyTimeIntervalTrigger());

        Action read = () => serializer.DeSerialize<DailyTimeIntervalTriggerImpl>(bytes);

        read.Should().Throw<Newtonsoft.Json.JsonSerializationException>()
            .WithInnerException<Newtonsoft.Json.JsonSerializationException>()
            .WithMessage("*Quartz.TimeOfDay*",
                "the read fails on the time of day and not on the zone, which is what makes this a separate defect");
    }

    private static DailyTimeIntervalTriggerImpl CreateDailyTimeIntervalTrigger()
    {
        return new DailyTimeIntervalTriggerImpl
        {
            Key = new TriggerKey("dailyTimeInterval", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            RepeatInterval = 42,
            RepeatIntervalUnit = IntervalUnit.Second,
            StartTimeOfDay = new TimeOfDay(3, 30),
            EndTimeOfDay = new TimeOfDay(4, 40),
            DaysOfWeek = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday },
            TimeZone = NonLocalZone,
            StartTimeUtc = startTime
        };
    }

    private static JObject Write<T>(T trigger, bool registerTriggerConverters) where T : class
    {
        JsonObjectSerializer serializer = CreateSerializer(registerTriggerConverters);
        return JObject.Parse(Encoding.UTF8.GetString(serializer.Serialize(trigger)));
    }

    private static T RoundTrip<T>(T trigger, bool registerTriggerConverters) where T : class, ITrigger
    {
        JsonObjectSerializer serializer = CreateSerializer(registerTriggerConverters);
        T restored = serializer.DeSerialize<T>(serializer.Serialize(trigger));

        restored.Should().NotBeNull();
        restored.Key.Should().Be(trigger.Key);
        restored.JobKey.Should().Be(trigger.JobKey);
        restored.StartTimeUtc.Should().Be(trigger.StartTimeUtc);
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
/// The literals are verbatim output of the code at <c>1ce49f80b</c> - <c>JsonObjectSerializer</c> with
/// its default settings, serializing a trigger whose zone was Tokyo - captured before the converter was
/// written and pasted in unedited, so "it still reads" is a fact about those bytes rather than a
/// reconstruction of them. The display and standard names are the Windows spellings the capturing
/// machine had; only <c>Id</c> is ever read, which is exactly why the rest of the object was worthless.
/// </para>
/// <para>
/// Nothing here carries a framework-qualified <c>$type</c>, so the same literals read on
/// <c>net472</c> and <c>net10.0</c> alike.
/// </para>
/// </remarks>
public class NewtonsoftLegacyTimeZonePayloadTest
{
    private const string LegacyCalendarIntervalTrigger =
        """{"$type":"Quartz.Impl.Triggers.CalendarIntervalTriggerImpl, Quartz","StartTimeUtc":"2024-07-01T00:00:00+00:00","HasMillisecondPrecision":true,"RepeatIntervalUnit":3,"RepeatInterval":3,"TimeZone":{"Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false},"PreserveHourOfDayAcrossDaylightSavings":false,"SkipDayIfHourDoesNotExist":false,"TimesTriggered":0,"Name":"calendarInterval","Group":"group","JobName":"job","JobGroup":"jobGroup","FullName":"group.calendarInterval","Key":{"Name":"calendarInterval","Group":"group"},"JobKey":{"Name":"job","Group":"jobGroup"},"FullJobName":"jobGroup.job","IsPreferredNodeAuto":false,"JobDataMap":{},"MisfireInstruction":0,"Priority":5,"HasAdditionalProperties":false}""";

    private const string LegacyRecurrenceTrigger =
        """{"$type":"Quartz.Impl.Triggers.RecurrenceTriggerImpl, Quartz","RecurrenceRule":"FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR","TimeZone":{"Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false},"TimesTriggered":3,"StartTimeUtc":"2024-07-01T00:00:00+00:00","HasMillisecondPrecision":false,"Name":"recurrence","Group":"group","JobName":"job","JobGroup":"jobGroup","FullName":"group.recurrence","Key":{"Name":"recurrence","Group":"group"},"JobKey":{"Name":"job","Group":"jobGroup"},"FullJobName":"jobGroup.job","IsPreferredNodeAuto":false,"JobDataMap":{},"MisfireInstruction":0,"Priority":5,"HasAdditionalProperties":false}""";

    private const string LegacyCronTrigger =
        """{"$type":"Quartz.Impl.Triggers.CronTriggerImpl, Quartz","CronExpressionString":"0/5 * * * * ?","StartTimeUtc":"2024-07-01T00:00:00+00:00","TimeZone":{"Id":"Tokyo Standard Time","HasIanaId":false,"DisplayName":"(UTC+09:00) Osaka, Sapporo, Tokyo","StandardName":"Tokyo Standard Time","DaylightName":"Tokyo Daylight Time","BaseUtcOffset":"09:00:00","SupportsDaylightSavingTime":false},"HasMillisecondPrecision":false,"Name":"cron","Group":"group","JobName":"job","JobGroup":"jobGroup","FullName":"group.cron","Key":{"Name":"cron","Group":"group"},"JobKey":{"Name":"job","Group":"jobGroup"},"FullJobName":"jobGroup.job","IsPreferredNodeAuto":false,"JobDataMap":{},"MisfireInstruction":0,"Priority":5,"HasAdditionalProperties":false}""";

    private static TimeZoneInfo Tokyo => TimeZoneUtil.FindTimeZoneById("Tokyo Standard Time");

    [Test]
    public void CalendarIntervalTriggerReadsTheTimeZoneObjectItUsedToBeWrittenWith()
    {
        CalendarIntervalTriggerImpl trigger = Deserialize<CalendarIntervalTriggerImpl>(LegacyCalendarIntervalTrigger);

        trigger.TimeZone.Should().Be(Tokyo);
        trigger.RepeatInterval.Should().Be(3, "the rest of the payload has to read as it always did");
        trigger.RepeatIntervalUnit.Should().Be(IntervalUnit.Hour);
    }

    [Test]
    public void RecurrenceTriggerReadsTheTimeZoneObjectItUsedToBeWrittenWith()
    {
        RecurrenceTriggerImpl trigger = Deserialize<RecurrenceTriggerImpl>(LegacyRecurrenceTrigger);

        trigger.TimeZone.Should().Be(Tokyo);
        trigger.RecurrenceRule.Should().Be("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR");
    }

    [Test]
    public void CronTriggerReadsTheTimeZoneObjectItUsedToBeWrittenWith()
    {
        CronTriggerImpl trigger = Deserialize<CronTriggerImpl>(LegacyCronTrigger);

        trigger.TimeZone.Should().Be(Tokyo,
            "the getter reads through to the expression once one exists, so this is also the assertion that the rebuilt expression carries the zone");
        trigger.CronExpressionString.Should().Be("0/5 * * * * ?");
    }

    private static T Deserialize<T>(string json) where T : class
    {
        // The default settings, which is what wrote these payloads.
        JsonObjectSerializer serializer = new JsonObjectSerializer();
        serializer.Initialize();
        return serializer.DeSerialize<T>(Encoding.UTF8.GetBytes(json));
    }
}

/// <summary>
/// The converter is attached to typed <see cref="TimeZoneInfo" /> members by
/// <c>QuartzContractResolver</c>, not registered on the serializer, and this is the difference that
/// makes: a trigger's <c>TimeZone</c> property is written as its id, while a zone sitting in a job data
/// map - where the slot is typed <see cref="object" /> - is written exactly as it always was.
/// </summary>
/// <remarks>
/// <para>
/// Scoping is what keeps that true: a converter on the serializer's list is consulted for a value's
/// runtime type wherever it appears, so a global one would have written the zone as a bare string and
/// dropped the <c>$type</c> that the object-typed path carries - changing the shape of every payload
/// already sitting in a job data column.
/// </para>
/// <para>
/// The written shape is asserted member by member rather than against a captured string, because most
/// of what Json.NET writes for a zone is the running OS's own text: Windows says
/// <c>"(UTC+09:00) Osaka, Sapporo, Tokyo"</c> where Linux says
/// <c>"(UTC+09:00) Japan Standard Time (Tokyo)"</c>, and the standard and daylight names differ with
/// them. Which members are written differs by target framework too - <c>HasIanaId</c> is .NET 6 and
/// later. Only <c>$type</c> and <c>Id</c> mean the same thing everywhere, and even the <c>$type</c>
/// names <c>System.Private.CoreLib</c> on <c>net10.0</c> and <c>mscorlib</c> on <c>net472</c>.
/// </para>
/// </remarks>
public class NewtonsoftJobDataTimeZoneTest
{
    [Test]
    public void AZoneInAJobDataMapIsWrittenAsItAlwaysWas()
    {
        JsonObjectSerializer serializer = NewtonsoftTriggerTimeZoneTest.CreateSerializer(registerTriggerConverters: false);
        TimeZoneInfo tokyo = TimeZoneUtil.FindTimeZoneById("Tokyo Standard Time");
        JobDataMap map = new JobDataMap();
        map.Put("zone", tokyo);

        JObject written = JObject.Parse(Encoding.UTF8.GetString(serializer.Serialize(map)));
        JToken zone = written["zone"];

        zone.Type.Should().Be(JTokenType.Object,
            "the converter is scoped to typed members, so an object-typed slot is left alone - a global one would have collapsed the zone to a bare string");
        zone["$type"].Value<string>().Should().StartWith("System.TimeZoneInfo,",
            "the $type is what every payload already in a job data column carries, and losing it would change the shape of a stored blob");
        zone["Id"].Value<string>().Should().Be(tokyo.Id,
            "the id is the one member of a written zone that means the same thing on every platform");
    }

    [Test]
    public void ATriggerWritesItsZoneAsTheIdInstead()
    {
        JsonObjectSerializer serializer = NewtonsoftTriggerTimeZoneTest.CreateSerializer(registerTriggerConverters: false);
        TimeZoneInfo tokyo = TimeZoneUtil.FindTimeZoneById("Tokyo Standard Time");
        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl
        {
            Key = new TriggerKey("calendarInterval", "group"),
            RepeatIntervalUnit = IntervalUnit.Hour,
            TimeZone = tokyo,
            StartTimeUtc = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero)
        };

        JObject written = JObject.Parse(Encoding.UTF8.GetString(serializer.Serialize(trigger)));
        JToken zone = written["TimeZone"];

        zone.Type.Should().Be(JTokenType.String,
            "a typed member is where the converter applies, so the whole object collapses to one value");
        zone.Value<string>().Should().Be(tokyo.Id,
            "the id is the only part of a zone another process can use, and its spelling is the resolving platform's own");
    }

    /// <summary>
    /// Reading that job data payload back has never worked: <c>IgnoreSerializableInterface</c> keeps
    /// Json.NET off <see cref="TimeZoneInfo" />'s <c>ISerializable</c> implementation, and every public
    /// member it writes instead is read-only, so there is nothing to construct the zone from. Recorded
    /// here because it is a limitation this change deliberately neither introduces nor repairs - the
    /// scoping is what proves it unchanged, and a job that needs a zone in its data should store the id.
    /// </summary>
    [Test]
    public void ReadingThatZoneBackHasNeverWorked()
    {
        JsonObjectSerializer serializer = NewtonsoftTriggerTimeZoneTest.CreateSerializer(registerTriggerConverters: false);
        JobDataMap map = new JobDataMap();
        map.Put("zone", TimeZoneUtil.FindTimeZoneById("Tokyo Standard Time"));
        byte[] bytes = serializer.Serialize(map);

        Action read = () => serializer.DeSerialize<JobDataMap>(bytes);

        read.Should().Throw<Newtonsoft.Json.JsonSerializationException>(
            "a TimeZoneInfo has no constructor Json.NET can call, which is why a job data map should hold the id and not the zone");
    }
}
