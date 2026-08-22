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

#nullable enable

using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <summary>
/// Pins the ISerializable shape of the calendar family — the binary contract for <c>CALENDARS</c>
/// blobs written by 1.x, 2.x and 3.x, which the binary-to-JSON migration path reads. BinaryFormatter
/// itself is gone from net10, so these tests drive the plumbing directly with hand-built
/// <see cref="SerializationInfo" /> and the non-public serialization constructors; a change that
/// breaks them breaks the ability to load stored calendars.
/// </summary>
/// <remarks>
/// <para>
/// The historical layouts are reconstructed here rather than read from stored <c>.ser</c> fixtures:
/// the repository keeps none, and a hand-built <see cref="SerializationInfo" /> states the entry
/// names and payload types a formatter would have written far more legibly than an opaque blob. The
/// current-format fixtures are generated from the current code by the round-trip tests, each of
/// which writes with <c>GetObjectData</c> and reads the result back through the serialization
/// constructor.
/// </para>
/// <para>
/// Two version numbers are in play in every payload. <c>baseCalendarVersion</c> belongs to
/// <see cref="BaseCalendar" /> and <c>version</c> to the subclass, and they moved independently —
/// which is why each subclass writes a version of its own even when the base did not change.
/// </para>
/// </remarks>
#pragma warning disable SYSLIB0050 // SerializationInfo/FormatterConverter are obsolete, which is the point: this exercises the legacy contract
public class CalendarBinarySerializationContractTest
{
    private static readonly Type[] calendarFamily =
    [
        typeof(BaseCalendar),
        typeof(AnnualCalendar),
        typeof(CronCalendar),
        typeof(DailyCalendar),
        typeof(HolidayCalendar),
        typeof(MonthlyCalendar),
        typeof(WeeklyCalendar)
    ];

    [TestCaseSource(nameof(calendarFamily))]
    public void EveryCalendarCarriesSerializableAttributeAndASerializationConstructor(Type calendarType)
    {
        calendarType.IsSerializable.Should().BeTrue("BinaryFormatter-written blobs record the type as serializable");

        SerializationConstructor(calendarType).Should().NotBeNull(
            "the (SerializationInfo, StreamingContext) constructor is what a formatter-style reader invokes");

        typeof(ISerializable).IsAssignableFrom(calendarType).Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // BaseCalendar — the entries every calendar in the family carries
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void BaseCalendarWritesTheFourEntriesTheBlobFormatHasAlwaysHad()
    {
        BaseCalendar calendar = new BaseCalendar(TimeZoneInfo.Utc) { Description = "business hours" };

        Dictionary<string, object?> entries = Write(calendar);

        entries.Keys.Should().BeEquivalentTo(["baseCalendarVersion", "baseCalendar", "description", "timeZoneId"],
            "readers of stored calendars look these entries up by exactly these names");

        entries["baseCalendarVersion"].Should().Be(1);
        entries["baseCalendar"].Should().BeNull();
        entries["description"].Should().Be("business hours");
        entries["timeZoneId"].Should().Be("UTC",
            "the zone travels as its id, never as a TimeZoneInfo — Windows and IANA ids differ and a "
            + "serialized TimeZoneInfo does not survive the crossing");
    }

    [Test]
    public void BaseCalendarWritesNoTimeZoneIdWhenNoZoneWasChosen()
    {
        Dictionary<string, object?> entries = Write(new BaseCalendar());

        entries["timeZoneId"].Should().BeNull(
            "unlike CronExpression, a calendar does not pin the writing machine's local zone; it stays "
            + "unset and resolves to the reading machine's local zone");
    }

    [Test]
    public void BaseCalendarRoundTripsThroughItsOwnSerializationPlumbing()
    {
        BaseCalendar chained = new BaseCalendar(TimeZoneInfo.Utc) { Description = "weekends" };
        BaseCalendar original = new BaseCalendar(chained, TimeZoneInfo.Utc) { Description = "holidays" };

        BaseCalendar deserialized = RoundTrip(original);

        deserialized.Description.Should().Be("holidays");
        deserialized.TimeZone.Should().Be(TimeZoneInfo.Utc);
        deserialized.CalendarBase.Should().BeOfType<BaseCalendar>()
            .Which.Description.Should().Be("weekends", "a chained calendar travels inside its parent's payload");
    }

    [Test]
    public void BaseCalendarReadsTheVersion0LayoutWithTheTimeZoneAsAnObject()
    {
        // The 1.x-era shape: no baseCalendarVersion entry, and the zone as a serialized TimeZoneInfo.
        SerializationInfo info = CreateInfo(typeof(BaseCalendar));
        info.AddValue("timeZone", TimeZoneInfo.Utc);
        info.AddValue("baseCalendar", null, typeof(ICalendar));
        info.AddValue("description", "legacy");

        BaseCalendar deserialized = (BaseCalendar) Read(typeof(BaseCalendar), info);

        deserialized.TimeZone.Should().Be(TimeZoneInfo.Utc);
        deserialized.Description.Should().Be("legacy");
    }

    [Test]
    public void BaseCalendarReadsThePre20LayoutWithBaseClassQualifiedEntryNames()
    {
        // The oldest shape: the base class's fields were serialized under a 'BaseCalendar+' prefix by
        // field-based serialization of the subclass, so nothing is present under the bare names.
        SerializationInfo info = CreateInfo(typeof(BaseCalendar));
        info.AddValue("BaseCalendar+timeZone", TimeZoneInfo.Utc);
        info.AddValue("BaseCalendar+baseCalendar", null, typeof(ICalendar));
        info.AddValue("BaseCalendar+description", "prefixed");

        BaseCalendar deserialized = (BaseCalendar) Read(typeof(BaseCalendar), info);

        deserialized.TimeZone.Should().Be(TimeZoneInfo.Utc);
        deserialized.Description.Should().Be("prefixed",
            "the absence of an unqualified 'description' entry is what selects the prefixed layout");
    }

    [Test]
    public void BaseCalendarReadsTheVersion1LayoutByTimeZoneId()
    {
        SerializationInfo info = BaseEntries(timeZoneId: "Eastern Standard Time", description: "stored");

        BaseCalendar deserialized = (BaseCalendar) Read(typeof(BaseCalendar), info);

        deserialized.TimeZone.Should().Be(TimeZones.FindById("Eastern Standard Time"),
            "the id is resolved through TimeZones.FindById, which is what makes a blob written on "
            + "Windows readable on Linux");
        deserialized.Description.Should().Be("stored");
    }

    [Test]
    public void BaseCalendarRejectsAnUnknownVersion()
    {
        SerializationInfo info = CreateInfo(typeof(BaseCalendar));
        info.AddValue("baseCalendarVersion", 2);
        info.AddValue("timeZoneId", "UTC");
        info.AddValue("baseCalendar", null, typeof(ICalendar));
        info.AddValue("description", null, typeof(string));

        Action act = () => Read(typeof(BaseCalendar), info);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>()
            .WithMessage("*Unknown serialization version*");
    }

    // ---------------------------------------------------------------------------------------------
    // AnnualCalendar — three payload shapes for the excluded days
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void AnnualCalendarWritesTheVersion2LayoutPinnedToALeapYear()
    {
        AnnualCalendar calendar = new AnnualCalendar();
        calendar.AddExcludedDay(new MonthDay(2, 29));
        calendar.AddExcludedDay(new MonthDay(12, 25));

        Dictionary<string, object?> entries = Write(calendar);

        entries["version"].Should().Be(2);
        entries["excludeDays"].Should().BeOfType<SortedSet<DateTime>>(
            "the payload entry must serialize as SortedSet<DateTime>; a different backing type emits a "
            + "type record older readers reject");

        ((SortedSet<DateTime>) entries["excludeDays"]!).Should().BeEquivalentTo(
            [new DateTime(2000, 2, 29), new DateTime(2000, 12, 25)],
            "the dates are pinned to a leap year so that February 29th survives the round-trip");
    }

    [Test]
    public void AnnualCalendarRoundTripsThroughItsOwnSerializationPlumbing()
    {
        AnnualCalendar original = new AnnualCalendar();
        original.AddExcludedDay(new MonthDay(2, 29));
        original.AddExcludedDay(new MonthDay(7, 4));

        RoundTrip(original).DaysExcluded.Should().BeEquivalentTo([new MonthDay(2, 29), new MonthDay(7, 4)]);
    }

    [Test]
    public void AnnualCalendarReadsTheVersion0ArrayListLayout()
    {
        // The 1.x shape: no version entry and an ArrayList of DateTime.
        SerializationInfo info = BaseEntries();
        info.AddValue("excludeDays", new ArrayList { new DateTime(2005, 7, 4), new DateTime(2005, 12, 25) });

        AnnualCalendar deserialized = (AnnualCalendar) Read(typeof(AnnualCalendar), info);

        deserialized.DaysExcluded.Should().BeEquivalentTo([new MonthDay(7, 4), new MonthDay(12, 25)],
            "only the month and day were ever meaningful, whatever year the stored value carried");
    }

    [Test]
    public void AnnualCalendarReadsTheVersion0DateTimeOffsetLayout()
    {
        // The same version number covered a second shape, written after the list became generic.
        SerializationInfo info = BaseEntries();
        info.AddValue(
            "excludeDays",
            new List<DateTimeOffset> { new DateTimeOffset(2005, 7, 4, 0, 0, 0, TimeSpan.Zero) },
            typeof(object));

        AnnualCalendar deserialized = (AnnualCalendar) Read(typeof(AnnualCalendar), info);

        deserialized.DaysExcluded.Should().BeEquivalentTo([new MonthDay(7, 4)]);
    }

    [Test]
    public void AnnualCalendarReadsTheVersion1DateTimeOffsetLayout()
    {
        SerializationInfo info = BaseEntries();
        info.AddValue("version", 1);
        info.AddValue("excludeDays", new List<DateTimeOffset> { new DateTimeOffset(2005, 12, 25, 0, 0, 0, TimeSpan.Zero) });

        AnnualCalendar deserialized = (AnnualCalendar) Read(typeof(AnnualCalendar), info);

        deserialized.DaysExcluded.Should().BeEquivalentTo([new MonthDay(12, 25)]);
    }

    [Test]
    public void AnnualCalendarReadsTheVersion2SortedSetLayout()
    {
        SerializationInfo info = BaseEntries();
        info.AddValue("version", 2);
        info.AddValue("excludeDays", new SortedSet<DateTime> { new DateTime(2000, 2, 29) });

        AnnualCalendar deserialized = (AnnualCalendar) Read(typeof(AnnualCalendar), info);

        deserialized.DaysExcluded.Should().BeEquivalentTo([new MonthDay(2, 29)]);
    }

    [Test]
    public void AnnualCalendarRejectsAnUnknownVersion()
    {
        SerializationInfo info = BaseEntries();
        info.AddValue("version", 3);
        info.AddValue("excludeDays", new SortedSet<DateTime>());

        Action act = () => Read(typeof(AnnualCalendar), info);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>()
            .WithMessage("*Unknown serialization version*");
    }

    // ---------------------------------------------------------------------------------------------
    // DailyCalendar — the eight-integer time range
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void DailyCalendarWritesTheEightIntegerRangeLayout()
    {
        DailyCalendar calendar = new DailyCalendar(new TimeOnly(1, 20, 1, 456), new TimeOnly(14, 50, 15, 2))
        {
            InvertTimeRange = true
        };

        Dictionary<string, object?> entries = Write(calendar);

        entries["version"].Should().Be(1);
        entries.Should().Contain(new Dictionary<string, object?>
        {
            ["rangeStartingHourOfDay"] = 1,
            ["rangeStartingMinute"] = 20,
            ["rangeStartingSecond"] = 1,
            ["rangeStartingMillis"] = 456,
            ["rangeEndingHourOfDay"] = 14,
            ["rangeEndingMinute"] = 50,
            ["rangeEndingSecond"] = 15,
            ["rangeEndingMillis"] = 2,
            ["invertTimeRange"] = true
        }, "the range has always been eight separate integers, not a TimeOnly pair");
    }

    [Test]
    public void DailyCalendarRoundTripsThroughItsOwnSerializationPlumbing()
    {
        DailyCalendar original = new DailyCalendar(new TimeOnly(8, 0, 0, 500), new TimeOnly(17, 30, 15, 250))
        {
            InvertTimeRange = true
        };

        DailyCalendar deserialized = RoundTrip(original);

        deserialized.TimeRange.Should().Be(new TimeRange(new TimeOnly(8, 0, 0, 500), new TimeOnly(17, 30, 15, 250)),
            "the millisecond is the finest resolution the serialized form carries, and it must survive");
        deserialized.InvertTimeRange.Should().BeTrue();
    }

    [TestCase(null, TestName = "DailyCalendarReadsTheEightIntegerLayout(version 0)")]
    [TestCase(1, TestName = "DailyCalendarReadsTheEightIntegerLayout(version 1)")]
    public void DailyCalendarReadsTheEightIntegerLayout(int? version)
    {
        SerializationInfo info = BaseEntries();
        if (version is not null)
        {
            info.AddValue("version", version.Value);
        }

        info.AddValue("rangeStartingHourOfDay", 8);
        info.AddValue("rangeStartingMinute", 15);
        info.AddValue("rangeStartingSecond", 30);
        info.AddValue("rangeStartingMillis", 125);
        info.AddValue("rangeEndingHourOfDay", 17);
        info.AddValue("rangeEndingMinute", 45);
        info.AddValue("rangeEndingSecond", 0);
        info.AddValue("rangeEndingMillis", 0);
        info.AddValue("invertTimeRange", false);

        DailyCalendar deserialized = (DailyCalendar) Read(typeof(DailyCalendar), info);

        deserialized.TimeRange.Should().Be(new TimeRange(new TimeOnly(8, 15, 30, 125), new TimeOnly(17, 45)));
        deserialized.InvertTimeRange.Should().BeFalse();
    }

    [Test]
    public void DailyCalendarRejectsAnUnknownVersion()
    {
        SerializationInfo info = BaseEntries();
        info.AddValue("version", 2);

        Action act = () => Read(typeof(DailyCalendar), info);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>()
            .WithMessage("*Unknown serialization version*");
    }

    // ---------------------------------------------------------------------------------------------
    // HolidayCalendar — the one calendar that refuses its own oldest payloads
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void HolidayCalendarWritesTheVersion2DateTimeArrayLayout()
    {
        HolidayCalendar calendar = new HolidayCalendar();
        calendar.AddExcludedDay(new DateOnly(2026, 12, 25));

        Dictionary<string, object?> entries = Write(calendar);

        entries["version"].Should().Be(2);
        entries["dates"].Should().BeOfType<DateTime[]>(
            "the payload entry must serialize as DateTime[]; a different backing type emits a type "
            + "record older readers reject");

        ((DateTime[]) entries["dates"]!).Should().Equal([new DateTime(2026, 12, 25)]);
    }

    [Test]
    public void HolidayCalendarRoundTripsThroughItsOwnSerializationPlumbing()
    {
        HolidayCalendar original = new HolidayCalendar();
        original.AddExcludedDay(new DateOnly(2026, 12, 25));
        original.AddExcludedDay(new DateOnly(2027, 1, 1));

        RoundTrip(original).DaysExcluded.Should().BeEquivalentTo(
            [new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1)]);
    }

    [Test]
    public void HolidayCalendarReadsTheVersion2DateTimeArrayLayout()
    {
        SerializationInfo info = BaseEntries();
        info.AddValue("version", 2);
        info.AddValue("dates", new[] { new DateTime(2026, 12, 25) });

        HolidayCalendar deserialized = (HolidayCalendar) Read(typeof(HolidayCalendar), info);

        deserialized.DaysExcluded.Should().BeEquivalentTo([new DateOnly(2026, 12, 25)]);
    }

    [TestCase(null, TestName = "HolidayCalendarRefusesItsPre2xPayloads(version 0)")]
    [TestCase(1, TestName = "HolidayCalendarRefusesItsPre2xPayloads(version 1)")]
    public void HolidayCalendarRefusesItsPre2xPayloads(int? version)
    {
        SerializationInfo info = BaseEntries();
        if (version is not null)
        {
            info.AddValue("version", version.Value);
        }

        Action act = () => Read(typeof(HolidayCalendar), info);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>()
            .WithMessage("*use latest Quartz 2.x version to re-serialize*",
                "the advice names the way out, because the payload genuinely cannot be read here");
    }

    [Test]
    public void HolidayCalendarRejectsAnUnknownVersion()
    {
        SerializationInfo info = BaseEntries();
        info.AddValue("version", 3);
        info.AddValue("dates", Array.Empty<DateTime>());

        Action act = () => Read(typeof(HolidayCalendar), info);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>()
            .WithMessage("*Unknown serialization version*");
    }

    // ---------------------------------------------------------------------------------------------
    // The remaining family members: bool-array day masks, and a nested CronExpression
    // ---------------------------------------------------------------------------------------------

    [Test]
    public void MonthlyCalendarWritesTheBoolArrayDayMask()
    {
        MonthlyCalendar calendar = new MonthlyCalendar();
        calendar.AddExcludedDay(1);
        calendar.AddExcludedDay(31);

        Dictionary<string, object?> entries = Write(calendar);

        entries["version"].Should().Be(1);
        entries["excludeAll"].Should().Be(false);

        bool[] mask = entries["excludeDays"].Should().BeOfType<bool[]>().Subject;
        mask.Should().HaveCount(31, "the mask is indexed by day-of-month minus one and always covers 31 days");
        mask[0].Should().BeTrue();
        mask[30].Should().BeTrue();
        mask[15].Should().BeFalse();
    }

    [TestCase(null, TestName = "MonthlyCalendarReadsTheBoolArrayDayMask(version 0)")]
    [TestCase(1, TestName = "MonthlyCalendarReadsTheBoolArrayDayMask(version 1)")]
    public void MonthlyCalendarReadsTheBoolArrayDayMask(int? version)
    {
        bool[] mask = new bool[31];
        mask[0] = true;
        mask[30] = true;

        SerializationInfo info = BaseEntries();
        if (version is not null)
        {
            info.AddValue("version", version.Value);
        }

        info.AddValue("excludeDays", mask);
        info.AddValue("excludeAll", false);

        MonthlyCalendar deserialized = (MonthlyCalendar) Read(typeof(MonthlyCalendar), info);

        deserialized.DaysExcluded.Should().BeEquivalentTo([1, 31]);
    }

    [Test]
    public void WeeklyCalendarWritesTheBoolArrayDayMask()
    {
        WeeklyCalendar calendar = new WeeklyCalendar();

        Dictionary<string, object?> entries = Write(calendar);

        entries["version"].Should().Be(1);
        entries["excludeAll"].Should().Be(false);

        bool[] mask = entries["excludeDays"].Should().BeOfType<bool[]>().Subject;
        mask.Should().HaveCount(7, "the mask is indexed by DayOfWeek, Sunday first");
        mask[(int) DayOfWeek.Saturday].Should().BeTrue("a new WeeklyCalendar excludes the weekend");
        mask[(int) DayOfWeek.Sunday].Should().BeTrue();
        mask[(int) DayOfWeek.Wednesday].Should().BeFalse();
    }

    [TestCase(null, TestName = "WeeklyCalendarReadsTheBoolArrayDayMask(version 0)")]
    [TestCase(1, TestName = "WeeklyCalendarReadsTheBoolArrayDayMask(version 1)")]
    public void WeeklyCalendarReadsTheBoolArrayDayMask(int? version)
    {
        bool[] mask = new bool[7];
        mask[(int) DayOfWeek.Monday] = true;

        SerializationInfo info = BaseEntries();
        if (version is not null)
        {
            info.AddValue("version", version.Value);
        }

        info.AddValue("excludeDays", mask);
        info.AddValue("excludeAll", false);

        WeeklyCalendar deserialized = (WeeklyCalendar) Read(typeof(WeeklyCalendar), info);

        deserialized.DaysExcluded.Should().BeEquivalentTo([DayOfWeek.Monday],
            "the stored mask replaces the weekend default rather than adding to it");
    }

    [Test]
    public void CronCalendarWritesTheNestedCronExpression()
    {
        CronCalendar calendar = new CronCalendar(null, "* * 0-7,18-23 ? * *") { TimeZone = TimeZoneInfo.Utc };

        Dictionary<string, object?> entries = Write(calendar);

        entries["version"].Should().Be(1);
        entries["cronExpression"].Should().BeOfType<CronExpression>(
            "the expression is nested as an object, so CronExpression's own ISerializable contract is "
            + "part of this calendar's blob format");
    }

    [Test]
    public void CronCalendarRoundTripsThroughItsOwnSerializationPlumbing()
    {
        // The zone rides in the nested expression, which is where both the constructor and the
        // property setter put it — CronCalendar reads TimeZone back off that expression.
        CronCalendar original = new CronCalendar(null, "* * 0-7,18-23 ? * *", TimeZoneInfo.Utc);

        CronCalendar deserialized = RoundTrip(original);

        deserialized.CronExpression.CronExpressionString.Should().Be("* * 0-7,18-23 ? * *");
        deserialized.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The <see cref="BaseCalendar" /> half of a payload, in the version 1 layout every subclass
    /// test builds on. Subclass entries are added on top by the caller.
    /// </summary>
    private static SerializationInfo BaseEntries(string? timeZoneId = "UTC", string? description = null)
    {
        SerializationInfo info = CreateInfo(typeof(BaseCalendar));
        info.AddValue("baseCalendarVersion", 1);
        info.AddValue("timeZoneId", timeZoneId, typeof(string));
        info.AddValue("baseCalendar", null, typeof(ICalendar));
        info.AddValue("description", description, typeof(string));
        return info;
    }

    private static SerializationInfo CreateInfo(Type type)
    {
        return new SerializationInfo(type, new FormatterConverter());
    }

    private static Dictionary<string, object?> Write(BaseCalendar calendar)
    {
        SerializationInfo info = CreateInfo(calendar.GetType());
        ((ISerializable) calendar).GetObjectData(info, default);

        Dictionary<string, object?> entries = new Dictionary<string, object?>();
        SerializationInfoEnumerator enumerator = info.GetEnumerator();
        while (enumerator.MoveNext())
        {
            entries[enumerator.Name] = enumerator.Value;
        }

        return entries;
    }

    private static T RoundTrip<T>(T calendar) where T : BaseCalendar
    {
        SerializationInfo info = CreateInfo(typeof(T));
        ((ISerializable) calendar).GetObjectData(info, default);
        return (T) Read(typeof(T), info);
    }

    private static ConstructorInfo SerializationConstructor(Type calendarType)
    {
        return calendarType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(SerializationInfo), typeof(StreamingContext)])!;
    }

    private static object Read(Type calendarType, SerializationInfo info)
    {
        return SerializationConstructor(calendarType).Invoke([info, default(StreamingContext)]);
    }
}
#pragma warning restore SYSLIB0050
