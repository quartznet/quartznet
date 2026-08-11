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

namespace Quartz.Tests.Unit;

/// <summary>
/// Pins the ISerializable shape of <see cref="JobDataMap" /> — the binary contract for
/// <c>JOB_DATA</c> and <c>BLOB_TRIGGERS</c> blobs written by 3.x, which the binary-to-JSON
/// migration path reads. BinaryFormatter itself is gone from net10, so these tests drive the
/// plumbing directly with hand-built <see cref="SerializationInfo" /> and the private
/// serialization constructor; a change that breaks them breaks the ability to load 3.x blobs.
/// </summary>
#pragma warning disable SYSLIB0050 // SerializationInfo/FormatterConverter are obsolete, which is the point: this exercises the legacy contract
public class JobDataMapBinarySerializationContractTest
{
    [Test]
    public void JobDataMapCarriesSerializableAttributeAndPrivateSerializationConstructor()
    {
        typeof(JobDataMap).IsSerializable.Should().BeTrue("BinaryFormatter-written blobs record the type as serializable");

        SerializationConstructor().Should().NotBeNull(
            "the (SerializationInfo, StreamingContext) constructor is what a formatter-style reader invokes");
    }

    [Test]
    public void GetObjectDataWritesTheThreeEntriesTheBlobFormatHasAlwaysHad()
    {
        JobDataMap map = new JobDataMap();
        map["environment"] = "staging";
        map["retryCount"] = 3;

        SerializationInfo info = CreateInfo();
        ((ISerializable) map).GetObjectData(info, default);

        Dictionary<string, object?> entries = ToDictionary(info);
        entries.Keys.Should().BeEquivalentTo(["version", "dirty", "map"],
            "3.x readers look these entries up by exactly these names");

        entries["version"].Should().Be(1);
        entries["dirty"].Should().Be(true, "the map was modified after construction");
        entries["map"].Should().BeOfType<Dictionary<string, object?>>(
            "the payload entry must serialize as Dictionary<string, object>; a different backing type emits a type record 3.x readers reject");

        Dictionary<string, object?> payload = (Dictionary<string, object?>) entries["map"]!;
        payload.Should().HaveCount(2);
        payload["environment"].Should().Be("staging");
        payload["retryCount"].Should().Be(3);
    }

    [Test]
    public void CleanMapWritesDirtyFalse()
    {
        JobDataMap map = new JobDataMap(new Dictionary<string, object?> { ["key"] = "value" });

        SerializationInfo info = CreateInfo();
        ((ISerializable) map).GetObjectData(info, default);

        ToDictionary(info)["dirty"].Should().Be(false);
    }

    [Test]
    public void RoundTripsThroughItsOwnSerializationPlumbing()
    {
        JobDataMap original = new JobDataMap();
        original["environment"] = "staging";
        original["retryCount"] = 3;

        SerializationInfo info = CreateInfo();
        ((ISerializable) original).GetObjectData(info, default);
        JobDataMap deserialized = InvokeSerializationConstructor(info);

        deserialized.Should().HaveCount(2);
        deserialized.GetString("environment").Should().Be("staging");
        deserialized.GetInt("retryCount").Should().Be(3);
        deserialized.Dirty.Should().BeTrue("the dirty flag itself is part of the serialized state");
    }

    [Test]
    public void ReadsTheVersion1FormatEveryVersionSince20Wrote()
    {
        SerializationInfo info = CreateInfo();
        info.AddValue("version", 1);
        info.AddValue("dirty", false);
        info.AddValue("map", new Dictionary<string, object?> { ["environment"] = "staging" });

        JobDataMap deserialized = InvokeSerializationConstructor(info);

        deserialized.GetString("environment").Should().Be("staging");
        deserialized.Dirty.Should().BeFalse();
    }

    [Test]
    public void ReadsTheVersion0FormatWithUnqualifiedEntryNames()
    {
        // The 1.x-era shape after the map became generic: no version entry, field-named entries.
        SerializationInfo info = CreateInfo();
        info.AddValue("dirty", true);
        info.AddValue("map", new Dictionary<string, object?> { ["environment"] = "staging" });

        JobDataMap deserialized = InvokeSerializationConstructor(info);

        deserialized.GetString("environment").Should().Be("staging");
        deserialized.Dirty.Should().BeFalse("the version 0 reader never restored the flag");
    }

    [Test]
    public void ReadsThePre20FormatWithBaseClassQualifiedHashtable()
    {
        // The oldest shape: entries qualified 'DirtyFlagMap+' by base-class field serialization,
        // with a Hashtable payload.
        Hashtable payload = new Hashtable
        {
            ["environment"] = "staging",
            ["retryCount"] = 3
        };

        SerializationInfo info = CreateInfo();
        info.AddValue("DirtyFlagMap+map", payload);

        JobDataMap deserialized = InvokeSerializationConstructor(info);

        deserialized.GetString("environment").Should().Be("staging");
        deserialized.GetInt("retryCount").Should().Be(3);
    }

    [Test]
    public void RejectsAnUnknownVersion()
    {
        SerializationInfo info = CreateInfo();
        info.AddValue("version", 2);
        info.AddValue("dirty", false);
        info.AddValue("map", new Dictionary<string, object?>());

        Action act = () => InvokeSerializationConstructor(info);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NotSupportedException>()
            .WithMessage("*Unknown serialization version*");
    }

    private static SerializationInfo CreateInfo()
    {
        return new SerializationInfo(typeof(JobDataMap), new FormatterConverter());
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
        return typeof(JobDataMap).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(SerializationInfo), typeof(StreamingContext)])!;
    }

    private static JobDataMap InvokeSerializationConstructor(SerializationInfo info)
    {
        return (JobDataMap) SerializationConstructor().Invoke([info, default(StreamingContext)]);
    }
}
#pragma warning restore SYSLIB0050
