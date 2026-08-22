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

using System.Reflection;
using System.Runtime.Serialization;

namespace Quartz.Tests.Unit;

/// <summary>
/// Pins the ISerializable shape of <see cref="CronExpression" /> — the binary contract for the
/// cron expression inside a 3.x <c>BLOB_TRIGGERS</c> or <c>CALENDARS</c> blob, which the
/// binary-to-JSON migration path reads. BinaryFormatter itself is gone from net10, so these tests
/// drive the plumbing directly with hand-built <see cref="SerializationInfo" /> and the private
/// serialization constructor; a change that breaks them breaks the ability to load 3.x blobs.
/// </summary>
/// <remarks>
/// The historical layouts are reconstructed here rather than read from stored <c>.ser</c> fixtures:
/// the repository keeps none, and a hand-built <see cref="SerializationInfo" /> states the field
/// names and payload types a formatter would have written far more legibly than an opaque blob.
/// The current-format fixture is generated from the current code by
/// <see cref="RoundTripsThroughItsOwnSerializationPlumbing" />.
/// </remarks>
#pragma warning disable SYSLIB0050 // SerializationInfo/FormatterConverter are obsolete, which is the point: this exercises the legacy contract
public class CronExpressionBinarySerializationContractTest
{
    [Test]
    public void CronExpressionCarriesSerializableAttributeAndPrivateSerializationConstructor()
    {
        typeof(CronExpression).IsSerializable.Should().BeTrue("BinaryFormatter-written blobs record the type as serializable");

        SerializationConstructor().Should().NotBeNull(
            "the (SerializationInfo, StreamingContext) constructor is what a formatter-style reader invokes");
    }

    [Test]
    public void GetObjectDataWritesTheThreeEntriesTheBlobFormatHasAlwaysHad()
    {
        CronExpression expression = new CronExpression("0 15 10 * * ?", TimeZoneInfo.Utc);

        SerializationInfo info = CreateInfo();
        ((ISerializable) expression).GetObjectData(info, default);

        Dictionary<string, object?> entries = ToDictionary(info);
        entries.Keys.Should().BeEquivalentTo(["version", "cronExpression", "timeZoneId"],
            "3.x readers look these entries up by exactly these names");

        entries["version"].Should().Be(1);
        entries["cronExpression"].Should().Be("0 15 10 * * ?",
            "the expression string is the whole of the persisted state; everything else is re-parsed");
        entries["timeZoneId"].Should().Be("UTC",
            "the zone travels as its id, never as a TimeZoneInfo — Windows and IANA ids differ and a "
            + "serialized TimeZoneInfo does not survive the crossing");
    }

    [Test]
    public void GetObjectDataUpperCasesTheExpressionTheSameWayTheConstructorDid()
    {
        CronExpression expression = new CronExpression("0 0 12 ? * mon-fri", TimeZoneInfo.Utc);

        SerializationInfo info = CreateInfo();
        ((ISerializable) expression).GetObjectData(info, default);

        ToDictionary(info)["cronExpression"].Should().Be("0 0 12 ? * MON-FRI",
            "the stored string is the normalized one, so re-parsing it on the way back cannot change meaning");
    }

    [Test]
    public void GetObjectDataResolvesTheLocalZoneRatherThanWritingNothing()
    {
        CronExpression expression = new CronExpression("0 15 10 * * ?");

        SerializationInfo info = CreateInfo();
        ((ISerializable) expression).GetObjectData(info, default);

        ToDictionary(info)["timeZoneId"].Should().Be(TimeZoneInfo.Local.Id,
            "an expression with no explicit zone runs in the local one, and the writer pins that "
            + "rather than leaving the reader to pick the reading machine's zone");
    }

    [Test]
    public void RoundTripsThroughItsOwnSerializationPlumbing()
    {
        CronExpression original = new CronExpression("0 15 10 * * ?", TimeZoneInfo.Utc);

        SerializationInfo info = CreateInfo();
        ((ISerializable) original).GetObjectData(info, default);
        CronExpression deserialized = InvokeSerializationConstructor(info);

        deserialized.CronExpressionString.Should().Be(original.CronExpressionString);
        deserialized.TimeZone.Should().Be(TimeZoneInfo.Utc);
        deserialized.Should().Be(original);
    }

    [Test]
    public void ReadsTheVersion1FormatEveryVersionSince20Wrote()
    {
        SerializationInfo info = CreateInfo();
        info.AddValue("version", 1);
        info.AddValue("cronExpression", "0 15 10 * * ?");
        info.AddValue("timeZoneId", "Eastern Standard Time");

        CronExpression deserialized = InvokeSerializationConstructor(info);

        deserialized.CronExpressionString.Should().Be("0 15 10 * * ?");
        deserialized.TimeZone.Should().Be(TimeZones.FindById("Eastern Standard Time"),
            "the id is resolved through TimeZones.FindById, which is what makes a blob written on "
            + "Windows readable on Linux");
    }

    [Test]
    public void Version1WithAnEmptyTimeZoneIdFallsBackToTheLocalZone()
    {
        SerializationInfo info = CreateInfo();
        info.AddValue("version", 1);
        info.AddValue("cronExpression", "0 15 10 * * ?");
        info.AddValue("timeZoneId", "");

        InvokeSerializationConstructor(info).TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    [Test]
    public void ReadsTheVersion0FormatWithTheTimeZoneAsAnObject()
    {
        // The 1.x-era shape: no version entry, the expression under its field name, and the zone as
        // a serialized TimeZoneInfo rather than an id.
        SerializationInfo info = CreateInfo();
        info.AddValue("cronExpressionString", "0 15 10 * * ?");
        info.AddValue("timeZone", TimeZoneInfo.Utc);

        CronExpression deserialized = InvokeSerializationConstructor(info);

        deserialized.CronExpressionString.Should().Be("0 15 10 * * ?");
        deserialized.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Test]
    public void RejectsAnUnknownVersion()
    {
        SerializationInfo info = CreateInfo();
        info.AddValue("version", 2);
        info.AddValue("cronExpression", "0 15 10 * * ?");
        info.AddValue("timeZoneId", "UTC");

        Action act = () => InvokeSerializationConstructor(info);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>()
            .WithMessage("*Unknown serialization version*");
    }

    /// <summary>
    /// Everything but the expression string and the zone is <c>[NonSerialized]</c> parse state, so the
    /// blob is only as good as the re-parse. These are the modifiers whose state lives outside the
    /// bitmask fields — an 'L' or 'W' or 'nth' that failed to come back would silently reschedule a job.
    /// </summary>
    [TestCase("0 15 10 * * ?")]
    [TestCase("0 0/5 14,18 * * ?")]
    [TestCase("0 0 12 L * ?")]
    [TestCase("0 0 12 LW * ?")]
    [TestCase("0 0 12 L-3 * ?")]
    [TestCase("0 0 12 15W * ?")]
    [TestCase("0 0 12 ? * 6#3")]
    [TestCase("0 0 12 ? * 6L")]
    [TestCase("0 0 12 ? * MON-FRI")]
    [TestCase("0 0 12 1/2 * ? 2026-2030")]
    public void ParseStateRebuiltFromTheExpressionStringSurvivesTheRoundTrip(string expressionString)
    {
        CronExpression original = new CronExpression(expressionString, TimeZoneInfo.Utc);

        SerializationInfo info = CreateInfo();
        ((ISerializable) original).GetObjectData(info, default);
        CronExpression deserialized = InvokeSerializationConstructor(info);

        DateTimeOffset cursor = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 25; i++)
        {
            DateTimeOffset? expected = original.GetNextValidTimeAfter(cursor);
            deserialized.GetNextValidTimeAfter(cursor).Should().Be(expected,
                $"fire time {i} after {cursor:O} must not move when '{expressionString}' comes back out of a blob");

            if (expected is null)
            {
                break;
            }

            cursor = expected.Value;
        }
    }

    private static SerializationInfo CreateInfo()
    {
        return new SerializationInfo(typeof(CronExpression), new FormatterConverter());
    }

    private static Dictionary<string, object?> ToDictionary(SerializationInfo info)
    {
        Dictionary<string, object?> entries = new Dictionary<string, object?>();
        SerializationInfoEnumerator enumerator = info.GetEnumerator();
        while (enumerator.MoveNext())
        {
            entries[enumerator.Name] = enumerator.Value;
        }

        return entries;
    }

    private static ConstructorInfo SerializationConstructor()
    {
        return typeof(CronExpression).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(SerializationInfo), typeof(StreamingContext)])!;
    }

    private static CronExpression InvokeSerializationConstructor(SerializationInfo info)
    {
        return (CronExpression) SerializationConstructor().Invoke([info, default(StreamingContext)]);
    }
}
#pragma warning restore SYSLIB0050
