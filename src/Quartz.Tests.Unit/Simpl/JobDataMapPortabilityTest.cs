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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A job data blob written by one serializer has to be readable by the other. Both are documented
/// store formats and an application is free to switch between them, so the column contents outlive
/// the choice that wrote them — and a value that survives its own author but not the other reader is
/// a one-way door nobody is warned about.
/// </summary>
/// <remarks>
/// The set covered here is the set <see cref="DataMapExtensions" /> declares an accessor for: those
/// are the types Quartz teaches an application to store, and so the ones the store format owes an
/// answer for. Everything past them is the application's own choice — see
/// <c>QuartzStoreJsonContext</c>, whose remarks spell the closed read side out.
/// </remarks>
public class JobDataMapPortabilityTest
{
    private IObjectSerializer newtonsoftSerializer;
    private IObjectSerializer systemTextJsonSerializer;

    [SetUp]
    public void SetUp()
    {
        newtonsoftSerializer = new NewtonsoftJsonObjectSerializer();
        systemTextJsonSerializer = new SystemTextJsonObjectSerializer();
    }

    /// <summary>
    /// The name is what a failure reports, so each value is its own case rather than a loop.
    /// </summary>
    private static IEnumerable<TestCaseData> PortableValues()
    {
        yield return Case("string", "value", map => map.GetString("string").Should().Be("value"));
        yield return Case("bool", true, map => map.GetBoolean("bool").Should().BeTrue());
        yield return Case("char", 'x', map => map.GetChar("char").Should().Be('x'));
        yield return Case("int", 123, map => map.GetInt("int").Should().Be(123));
        yield return Case("long", 9_000_000_000L, map => map.GetLong("long").Should().Be(9_000_000_000L));
        yield return Case("float", 1.5f, map => map.GetFloat("float").Should().Be(1.5f));
        yield return Case("double", 12.34, map => map.GetDouble("double").Should().Be(12.34));
        yield return Case("decimal", 1.5m, map => map.GetDecimal("decimal").Should().Be(1.5m));
        yield return Case("dateTime", new DateTime(1982, 6, 28, 1, 1, 1, DateTimeKind.Unspecified), map => map.GetDateTime("dateTime").Should().Be(new DateTime(1982, 6, 28, 1, 1, 1, DateTimeKind.Unspecified)));
        yield return Case("dateTimeOffset", new DateTimeOffset(1982, 6, 28, 1, 1, 1, TimeSpan.FromHours(3)), map => map.GetDateTimeOffset("dateTimeOffset").Should().Be(new DateTimeOffset(1982, 6, 28, 1, 1, 1, TimeSpan.FromHours(3))));
        yield return Case("timeSpan", TimeSpan.FromMinutes(90), map => map.GetTimeSpan("timeSpan").Should().Be(TimeSpan.FromMinutes(90)));
        yield return Case("guid", Guid.Parse("11111111-2222-3333-4444-555555555555"), map => map.GetGuid("guid").Should().Be(Guid.Parse("11111111-2222-3333-4444-555555555555")));
        yield return Case("dateOnly", new DateOnly(1982, 6, 28), map => map.GetDateOnly("dateOnly").Should().Be(new DateOnly(1982, 6, 28)));
        yield return Case("timeOnly", new TimeOnly(1, 2, 3), map => map.GetTimeOnly("timeOnly").Should().Be(new TimeOnly(1, 2, 3)));
        yield return Case("enum", IntervalUnit.Hour, map => map.GetEnum<IntervalUnit>("enum").Should().Be(IntervalUnit.Hour));
        yield return Case("null", null, map => map["null"].Should().BeNull());
    }

    private static TestCaseData Case(string key, object value, Action<JobDataMap> assert)
    {
        return new TestCaseData(key, value, assert).SetName("{m}(" + key + ")");
    }

    [TestCaseSource(nameof(PortableValues))]
    public void AValueTheAccessorsCoverReadsBackWhicheverSerializerWroteIt(string key, object value, Action<JobDataMap> assert)
    {
        JobDataMap original = new JobDataMap { { key, value } };

        foreach ((string label, IObjectSerializer writer, IObjectSerializer reader) in Pairings())
        {
            JobDataMap restored = reader.Deserialize<JobDataMap>(writer.Serialize(original))!;

            restored.Should().ContainKey(key, "{0}: the key must survive the crossing", label);

            try
            {
                assert(restored);
            }
            catch (Exception e) when (e is not AssertionException)
            {
                throw new AssertionException($"{label}: reading '{key}' back threw {e.GetType().Name}: {e.Message}", e);
            }
        }
    }

    /// <summary>
    /// The dirty flag says "changed since it was written", so a map that has only just been read is
    /// clean — otherwise every load would schedule a needless write back.
    /// </summary>
    [Test]
    public void AMapJustReadIsNotDirtyWhicheverSerializerWroteIt()
    {
        JobDataMap original = new JobDataMap { { "key", "value" } };

        foreach ((string label, IObjectSerializer writer, IObjectSerializer reader) in Pairings())
        {
            JobDataMap restored = reader.Deserialize<JobDataMap>(writer.Serialize(original))!;

            restored.Dirty.Should().BeFalse("{0}: a map loaded from the store has not been modified since it was written", label);
        }
    }

    /// <summary>
    /// Nesting is where the two formats stop agreeing, so this is the guarantee an application gets:
    /// none. A nested map never comes back as the <see cref="JobDataMap" /> it went in as, and the two
    /// serializers do not agree on what it does come back as — System.Text.Json's read side is closed
    /// over the primitives plus <c>Dictionary&lt;string, string&gt;</c>, while Newtonsoft hands back
    /// its own <c>JObject</c>. Job code that needs structure has to serialize the structure itself and
    /// store the result as a string.
    /// </summary>
    [Test]
    public void ANestedMapIsNotPartOfThePortableFormat()
    {
        JobDataMap original = new JobDataMap { { "nested", new JobDataMap { { "inner", "value" } } } };

        foreach ((string label, IObjectSerializer writer, IObjectSerializer reader) in Pairings())
        {
            JobDataMap restored = reader.Deserialize<JobDataMap>(writer.Serialize(original))!;

            restored["nested"].Should().NotBeOfType<JobDataMap>(
                "{0}: neither format carries a nested map's identity, so job code must not expect one back", label);
        }
    }

    private IEnumerable<(string Label, IObjectSerializer Writer, IObjectSerializer Reader)> Pairings()
    {
        yield return ("newtonsoft -> newtonsoft", newtonsoftSerializer, newtonsoftSerializer);
        yield return ("newtonsoft -> system.text.json", newtonsoftSerializer, systemTextJsonSerializer);
        yield return ("system.text.json -> newtonsoft", systemTextJsonSerializer, newtonsoftSerializer);
        yield return ("system.text.json -> system.text.json", systemTextJsonSerializer, systemTextJsonSerializer);
    }
}
