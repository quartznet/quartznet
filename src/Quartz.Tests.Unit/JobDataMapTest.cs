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

using System.Collections;
using System.Globalization;

using AwesomeAssertions.Execution;

using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// Unit test for JobDataMap serialization backwards compatibility.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
[NonParallelizable]
public class JobDataMapTest : SerializationTestSupport<JobDataMap>
{
    public JobDataMapTest(Type serializerType) : base(serializerType)
    {
    }

    /// <summary>
    /// Get the object to serialize when generating serialized file for future
    /// tests, and against which to validate deserialized object.
    /// </summary>
    /// <returns></returns>
    protected override JobDataMap GetTargetObject()
    {
        JobDataMap m = new JobDataMap();
        m["key"] = 5;
        return m;
    }

    protected override void VerifyMatch(JobDataMap original, JobDataMap deserialized)
    {
        using (new AssertionScope())
        {
            deserialized.Should().NotBeNull();
            ((IDictionary<string, object>) deserialized).Should().BeEquivalentTo((IDictionary<string, object>) original);
            deserialized.Dirty.Should().BeFalse("should not be dirty when returning from serialization");
        }
    }

    [Test]
    public void HandlesGuid()
    {
        var map = new JobDataMap();
        map["key"] = Guid.NewGuid();
        using (new AssertionScope())
        {
            map.TryGetGuid("key", out var g).Should().BeTrue();
            g.Should().NotBe(Guid.Empty);

            map["key"] = Guid.NewGuid().ToString();
            map.TryGetGuid("key", out g).Should().BeTrue();
            g.Should().NotBe(Guid.Empty);
        }

    }

    [Test]
    public void PutAsString_StoresIntValueAsString()
    {
        string key = "testKey";
        int value = 123;

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString());
    }

    [Test]
    public void PutAsString_StoresDateTimeInRoundTripFormat()
    {
        string key = "testKey";
        DateTime value = new DateTime(2022, 1, 1, 15, 4, 5, 123, DateTimeKind.Utc).AddTicks(4567);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString("O", CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_DateTimeRoundTripsWithKindAndPrecision()
    {
        string key = "testKey";
        DateTime value = new DateTime(2022, 1, 1, 15, 4, 5, 123, DateTimeKind.Utc).AddTicks(4567);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.TryGetDateTime(key, out DateTime read).Should().BeTrue();
        read.Should().Be(value, "the 'O' format keeps sub-second precision");
        read.Kind.Should().Be(DateTimeKind.Utc, "RoundtripKind restores the Kind the writer had");
    }

    [Test]
    public void PutAsString_OverwritesExistingValue()
    {
        string key = "testKey";
        DateTime value1 = new DateTime(2021, 6, 15, 1, 2, 3, DateTimeKind.Unspecified);
        DateTime value2 = new DateTime(2022, 1, 1);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value1);
        map.PutAsString(key, value2);

        map.GetString(key).Should().Be(value2.ToString("O", CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_StoresDateTimeOffsetInRoundTripFormat()
    {
        string key = "testKey";
        DateTimeOffset value = new DateTimeOffset(2022, 1, 1, 15, 4, 5, 123, TimeSpan.FromHours(2)).AddTicks(4567);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString("O", CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_DateTimeOffsetRoundTripsWithOffsetAndPrecision()
    {
        string key = "testKey";
        DateTimeOffset value = new DateTimeOffset(2022, 1, 1, 15, 4, 5, 123, TimeSpan.FromHours(2)).AddTicks(4567);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.TryGetDateTimeOffset(key, out DateTimeOffset read).Should().BeTrue();
        read.Should().Be(value);
        read.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Test]
    public void TryGetDateTimeOffset_StillReadsTheGeneralFormat3xWrote()
    {
        // 3.x PutAsString wrote the invariant general format; values already in stores keep reading.
        DateTimeOffset value = new DateTimeOffset(2022, 1, 1, 15, 4, 5, TimeSpan.FromHours(2));
        JobDataMap map = new JobDataMap { ["legacy"] = value.ToString(CultureInfo.InvariantCulture) };

        map.TryGetDateTimeOffset("legacy", out DateTimeOffset read).Should().BeTrue();
        read.Should().Be(value);
    }

    [Test]
    public void TryGetDateTime_ReadsAZuluStringAsUtc()
    {
        // Behavioral change from 3.x: DateTimeStyles.None turned "…Z" into a local-shifted
        // Kind=Local value; RoundtripKind keeps the UTC clock reading and Kind=Utc.
        JobDataMap map = new JobDataMap { ["utc"] = "2026-01-02T15:04:05.1230000Z" };

        map.TryGetDateTime("utc", out DateTime read).Should().BeTrue();
        read.Kind.Should().Be(DateTimeKind.Utc);
        read.Should().Be(new DateTime(2026, 1, 2, 15, 4, 5, 123, DateTimeKind.Utc));
    }

    [Test]
    public void PutAsString_StoresDateOnlyAndTimeOnlyInRoundTripFormat()
    {
        JobDataMap map = new JobDataMap();
        map.PutAsString("date", new DateOnly(2022, 1, 31));
        map.PutAsString("time", new TimeOnly(15, 4, 5, 123));

        map.GetString("date").Should().Be("2022-01-31");
        map.GetString("time").Should().Be("15:04:05.1230000");

        map.TryGetDateOnly("date", out DateOnly date).Should().BeTrue();
        date.Should().Be(new DateOnly(2022, 1, 31));
        map.TryGetTimeOnly("time", out TimeOnly time).Should().BeTrue();
        time.Should().Be(new TimeOnly(15, 4, 5, 123));

        map.GetDateOnly("date").Should().Be(new DateOnly(2022, 1, 31));
        map.GetTimeOnly("time").Should().Be(new TimeOnly(15, 4, 5, 123));
    }

    [Test]
    public void EnumsRoundTripThroughPutAsString()
    {
        JobDataMap map = new JobDataMap();
        map.PutAsString("day", DayOfWeek.Monday);

        map.GetString("day").Should().Be("Monday");
        map.TryGetEnum("day", out DayOfWeek day).Should().BeTrue();
        day.Should().Be(DayOfWeek.Monday);
        map.GetEnum<DayOfWeek>("day").Should().Be(DayOfWeek.Monday);
    }

    [Test]
    public void TryGetEnum_AcceptsStoredEnumAndUnderlyingNumber()
    {
        JobDataMap map = new JobDataMap
        {
            ["boxed"] = DayOfWeek.Friday,
            ["number"] = (int) DayOfWeek.Friday,
            ["garbage"] = "NotADay"
        };

        map.TryGetEnum("boxed", out DayOfWeek boxed).Should().BeTrue();
        boxed.Should().Be(DayOfWeek.Friday);

        map.TryGetEnum("number", out DayOfWeek number).Should().BeTrue("a JSON round trip hands the underlying number back");
        number.Should().Be(DayOfWeek.Friday);

        map.TryGetEnum("garbage", out DayOfWeek _).Should().BeFalse();
    }

    [Test]
    public void TryGet_IsAPureTypeTest()
    {
        JobKey stored = new JobKey("job");
        JobDataMap map = new JobDataMap
        {
            ["key"] = stored,
            ["text"] = "42"
        };

        map.TryGet("key", out JobKey read).Should().BeTrue();
        read.Should().BeSameAs(stored);

        map.TryGet("text", out int _).Should().BeFalse("TryGet<T> never parses; use the typed accessors for that");
        map.TryGet("missing", out JobKey missing).Should().BeFalse();
        missing.Should().BeNull();
    }

    [Test]
    public void PutAsString_StoresTimeSpanValueAsString()
    {
        string key = "testKey";
        TimeSpan value = TimeSpan.FromHours(1);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString());
    }

    [Test]
    public void PutAsString_StoresDifferentTimeSpanValueAsString()
    {
        string key = "testKey";
        TimeSpan value = TimeSpan.FromMinutes(30);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString());
    }

    [Test]
    public void PutAsString_OverwritesExistingTimeSpanValue()
    {
        string key = "testKey";
        TimeSpan value1 = TimeSpan.FromHours(1);
        TimeSpan value2 = TimeSpan.FromMinutes(30);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value1);
        map.PutAsString(key, value2);

        map.GetString(key).Should().Be(value2.ToString());
    }

    [Test]
    public void PutAsString_StoresGuidValueAsString()
    {
        string key = "testKey";
        Guid value = Guid.NewGuid();

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString("N"));
        map.TryGetGuid(key, out Guid read).Should().BeTrue();
        read.Should().Be(value);
    }

    [Test]
    public void PutAsString_OverwritesExistingGuidValue()
    {
        string key = "testKey";
        Guid value1 = Guid.NewGuid();
        Guid value2 = new Guid("00000000-0000-0000-0000-000000000002");

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value1);
        map.PutAsString(key, value2);

        map.GetString(key).Should().Be(value2.ToString("N"));
    }

    [Test]
    public void GetDecimal_ReadsBackWhatTheMapWasGiven()
    {
        JobDataMap map = new JobDataMap
        {
            { "boxed", 12.34m },
            { "text", "56.78" },
            { "int", 9 }
        };

        map.GetDecimal("boxed").Should().Be(12.34m);
        map.GetDecimal("text").Should().Be(56.78m, "a decimal written through PutAsString has to come back");
        map.GetDecimal("int").Should().Be(9m);
    }

    [Test]
    public void TryGetDecimal_ReportsFailureInsteadOfThrowing()
    {
        JobDataMap map = new JobDataMap { { "text", "not a number" } };

        map.TryGetDecimal("text", out decimal value).Should().BeFalse();
        value.Should().Be(0m);

        map.TryGetDecimal("missing", out value).Should().BeFalse();
    }

    [Test]
    public void GetDecimal_ThrowsWhenTheValueIsNotADecimal()
    {
        JobDataMap map = new JobDataMap { { "text", "not a number" } };

        Action act = () => map.GetDecimal("text");
        act.Should().Throw<InvalidCastException>();
    }

    [Test]
    public void EqualsComparesValuesNotJustKeys()
    {
        JobDataMap first = new JobDataMap { { "key", "value" } };
        JobDataMap sameContent = new JobDataMap { { "key", "value" } };
        JobDataMap sameKeyDifferentValue = new JobDataMap { { "key", "other" } };

        first.Equals(sameContent).Should().BeTrue();
        first.GetHashCode().Should().Be(sameContent.GetHashCode(), "equal maps must hash equally");
        first.Equals(sameKeyDifferentValue).Should().BeFalse(
            "until 4.0 only the key sets were compared, so maps with different values counted as equal");
    }

    [Test]
    public void AssigningANestedMapWithDifferentValuesMarksTheOuterMapDirty()
    {
        JobDataMap outer = new JobDataMap { { "nested", new JobDataMap { { "key", "old" } } } };
        outer.ClearDirtyFlag();

        outer["nested"] = new JobDataMap { { "key", "new" } };

        outer.Dirty.Should().BeTrue(
            "the key-set-only equality used to suppress this, and the job store then skipped rewriting the changed data");
    }

    [Test]
    public void AssigningAnEqualValueDoesNotMarkTheMapDirty()
    {
        JobDataMap map = new JobDataMap { { "key", "value" } };
        map.ClearDirtyFlag();

        map["key"] = "value";

        map.Dirty.Should().BeFalse("writing back the value already there changes nothing worth persisting");
    }

    [Test]
    public void CanKeepDirtyFlagWhenSerializing()
    {
        Dictionary<string, object> dictionary = new Dictionary<string, object>();
        dictionary.Add("key", "value");

        new JobDataMap(dictionary).Dirty.Should().BeFalse();

        dictionary.Add(SchedulerConstants.ForceJobDataMapDirty, "true");
        var map = new JobDataMap(dictionary);
        map.Dirty.Should().BeTrue();
        map.Should().NotContainKey(SchedulerConstants.ForceJobDataMapDirty);
    }
}