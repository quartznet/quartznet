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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

using Quartz.Simpl;

namespace Quartz.Tests.Unit;

/// <summary>
/// Unit test for JobDataMap serialization backwards compatibility.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(BinaryObjectSerializer))]
[TestFixture(typeof(JsonObjectSerializer))]
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
        m.Put("key", 5);
        return m;
    }

    protected override void VerifyMatch(JobDataMap original, JobDataMap deserialized)
    {
        deserialized.Should().NotBeNull();
        deserialized.WrappedMap.Should().BeEquivalentTo(original.WrappedMap);
        if (serializer is not BinaryObjectSerializer)
        {
            deserialized.Dirty.Should().BeFalse("should not be dirty when returning from serialization");
        }
    }

    [Test]
    public void HandlesGuid()
    {
        var map = new JobDataMap();
        map["key"] = Guid.NewGuid();
        map.TryGetGuid("key", out var g).Should().BeTrue();
        g.Should().NotBe(Guid.Empty);

        map["key"] = Guid.NewGuid().ToString();
        map.TryGetGuidValue("key", out g).Should().BeTrue();
        g.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// A number a comma-decimal culture wrote is rejected by every numeric accessor, never read as a
    /// different number.
    /// </summary>
    /// <remarks>
    /// The invariant parser's default styles allow a group separator, which would read the comma in
    /// "3,14" as one and answer 314 — a hundredfold of the value, silently. The floating-point
    /// accessors therefore parse with styles that allow no group separator at all, so a string a
    /// comma-decimal culture wrote is unreadable as every numeric type alike, as it always was for
    /// <see cref="JobDataMap.GetIntValueFromString" />.
    /// </remarks>
    [Test]
    public void ACommaDecimalNumberIsRejectedByEveryNumericAccessor()
    {
        JobDataMap map = new JobDataMap();
        map.Put("pi", "3,14");

        Action readDouble = () => map.GetDoubleValueFromString("pi");
        readDouble.Should().Throw<FormatException>(
            "no numeric accessor allows a group separator, so the comma a de-DE machine wrote as a "
            + "decimal point makes the string unreadable rather than a hundredfold of itself");

        Action readFloat = () => map.GetFloatValueFromString("pi");
        readFloat.Should().Throw<FormatException>();

        Action readViaCoercingDouble = () => map.GetDoubleValue("pi");
        readViaCoercingDouble.Should().Throw<FormatException>("the coercing accessor reads a string through the same parser");

        Action readInt = () => map.GetIntValueFromString("pi");
        readInt.Should().Throw<FormatException>("the integer styles never allowed a group separator");
    }

    [Test]
    public void ADecimalPointNumberStillReadsWithTheInvariantCulture()
    {
        JobDataMap map = new JobDataMap();
        map.Put("pi", "3.14");
        map.Put("big", "-1.5e3");

        map.GetDoubleValueFromString("pi").Should().Be(3.14);
        map.GetFloatValueFromString("pi").Should().Be(3.14f);
        map.GetDoubleValueFromString("big").Should().Be(-1500d, "sign, decimal point and exponent are the whole of what a number written for a job may carry");
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
    public void PutAsString_StoresDateTimeValueAsString()
    {
        string key = "testKey";
        DateTime value = DateTime.Now;

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_StoresDifferentDateTimeValueAsString()
    {
        string key = "testKey";
        DateTime value = new DateTime(2022, 1, 1);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_OverwritesExistingValue()
    {
        string key = "testKey";
        DateTime value1 = DateTime.Now;
        DateTime value2 = new DateTime(2022, 1, 1);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value1);
        map.PutAsString(key, value2);

        map.GetString(key).Should().Be(value2.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_StoresDateTimeOffsetValueAsString()
    {
        string key = "testKey";
        DateTimeOffset value = DateTimeOffset.Now;

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_StoresDifferentDateTimeOffsetValueAsString()
    {
        string key = "testKey";
        DateTimeOffset value = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value.ToString(CultureInfo.InvariantCulture));
    }

    [Test]
    public void PutAsString_OverwritesExistingDateTimeOffsetValue()
    {
        string key = "testKey";
        DateTimeOffset value1 = DateTimeOffset.Now;
        DateTimeOffset value2 = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value1);
        map.PutAsString(key, value2);

        map.GetString(key).Should().Be(value2.ToString(CultureInfo.InvariantCulture));
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
    public void PutAsString_StoresNullableGuidValueAsString()
    {
        string key = "testKey";
        Guid? value = Guid.NewGuid();

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value?.ToString("N"));
    }

    [Test]
    public void PutAsString_StoresDifferentNullableGuidValueAsString()
    {
        string key = "testKey";
        Guid? value = new Guid("00000000-0000-0000-0000-000000000001");

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().Be(value?.ToString("N"));
    }

    [Test]
    public void PutAsString_OverwritesExistingNullableGuidValue()
    {
        string key = "testKey";
        Guid? value1 = Guid.NewGuid();
        Guid? value2 = new Guid("00000000-0000-0000-0000-000000000002");

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value1);
        map.PutAsString(key, value2);

        map.GetString(key).Should().Be(value2?.ToString("N"));
    }

    [Test]
    public void PutAsString_StoresNullGuidValueAsString()
    {
        string key = "testKey";
        Guid? value = null;

        JobDataMap map = new JobDataMap();
        map.PutAsString(key, value);

        map.GetString(key).Should().BeNull();
    }

    [Test]
    public void CanKeepDirtyFlagWhenSerializing()
    {
        IDictionary dictionary = new Dictionary<string, object>();
        dictionary.Add("key", "value");

        new JobDataMap(dictionary).Dirty.Should().BeFalse();

        dictionary.Add(SchedulerConstants.ForceJobDataMapDirty, "true");
        var map = new JobDataMap(dictionary);
        map.Dirty.Should().BeTrue();
        map.ContainsKey(SchedulerConstants.ForceJobDataMapDirty).Should().BeFalse();
    }
}