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
using System.Text.Json.Serialization;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Serialization.Newtonsoft;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A job data blob written by one serializer has to be readable by the other. Both are documented
/// store formats and an application is free to switch between them, so the column contents outlive
/// the choice that wrote them — and a value that survives its own author but not the other reader is
/// a one-way door nobody is warned about.
/// </summary>
/// <remarks>
/// The set covered here is the set <see cref="DataMapExtensions" /> declares an accessor for — those
/// are the types Quartz teaches an application to store, and so the ones the store format owes an
/// answer for — plus the one shape past them the format carries, a <c>Dictionary&lt;string, string&gt;</c>.
/// It has no accessor of its own, and it is the entry the two serializers disagreed about longest: one
/// wrote the type name beside it and the other did not, so a map written by the first and read by the
/// second came back with a type name sitting in the application's own key space.
/// Everything past that set is the application's own choice — see <c>JobDataValues</c>, which
/// declares what the store format will write and what it hands back, and refuses the rest at the one
/// moment anyone can still be told. Both serializers refuse against that same declaration, so the
/// negative cases below are asserted of both: a value only one of them accepts is a blob only one of
/// them can load.
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
        // The timestamp-shaped entry is not decoration: Json.NET parses a string that looks like one into
        // a DateTimeOffset wherever it reads a value, and rendering that back as a string would reformat
        // it — in the reading machine's culture, and with the reading machine's offset when the text
        // carried none. A string map has to hand back the strings that went into it.
        yield return Case("dictionary", new Dictionary<string, string> { ["one"] = "1", ["when"] = "1982-06-28T01:01:01+03:00" },
            map => map["dictionary"].Should().BeOfType<Dictionary<string, string>>(
                    "a string map is the one shape past the scalars the store format carries, and it has to come back as itself whichever serializer wrote it")
                .Which.Should().Equal(new Dictionary<string, string> { ["one"] = "1", ["when"] = "1982-06-28T01:01:01+03:00" }));
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
            // The pairing is half of what the failure has to say: the same value read back wrong is a
            // different bug depending on which serializer wrote the blob, so the label goes on both
            // the assertion failures and the throws.
            catch (AssertionException e)
            {
                throw new AssertionException($"{label}: {e.Message}", e);
            }
            catch (Exception e)
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
    /// The two serializers write a string map as the same bytes, which is the whole of why it crosses
    /// between them. Json.NET used to name the type it wrote the map under, and a reader that treats that
    /// name as data — as the built-in one did — handed the application a <c>$type</c> entry beside its own.
    /// </summary>
    [Test]
    public void AStringMapIsWrittenAsThePlainObjectByEitherSerializer()
    {
        JobDataMap original = new JobDataMap { { "dictionary", new Dictionary<string, string> { ["one"] = "1" } } };

        foreach ((string label, IObjectSerializer writer, string _) in Writers())
        {
            Encoding.UTF8.GetString(writer.Serialize(original)).Should().Be("""{"dictionary":{"one":"1"}}""",
                "{0}: a value both formats carry has to be the same bytes in both, or a blob written by one is a blob the other reads differently", label);
        }
    }

    /// <summary>
    /// The payloads that are already in databases: Json.NET wrote a string map with the type it had
    /// written it under, and both readers still have to hand back the map the application stored, without
    /// the type name in it.
    /// </summary>
    /// <remarks>
    /// The payload is written out here rather than produced by the current writer on purpose — it is the
    /// shape of a blob nothing writes any more, so a writer that changes again must not be able to change
    /// what this asserts.
    /// </remarks>
    [Test]
    public void ABlobThatNamesTheTypeBesideAStringMapStillReadsBackAsTheMap()
    {
        byte[] written = Encoding.UTF8.GetBytes(
            """{"dictionary":{"$type":"System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[System.String, System.Private.CoreLib]], System.Private.CoreLib","one":"1","when":"1982-06-28T01:01:01+03:00"}}""");

        foreach ((string label, IObjectSerializer reader) in Readers())
        {
            JobDataMap restored = reader.Deserialize<JobDataMap>(written)!;

            restored["dictionary"].Should().BeOfType<Dictionary<string, string>>("{0}: an upgrade does not get to make a stored map unreadable", label)
                .Which.Should().Equal(new Dictionary<string, string> { ["one"] = "1", ["when"] = "1982-06-28T01:01:01+03:00" },
                    "{0}: the type Json.NET wrote the map under is metadata, not an entry of the application's", label);
        }
    }

    /// <summary>
    /// <c>$type</c> is the name Json.NET writes a value's type under, so a map that stores an entry of its
    /// own under it has never crossed anywhere: Json.NET wrote the name twice and could not read its own
    /// blob back, and a map the built-in serializer wrote was one Json.NET could not load at all. Both
    /// refuse it now, at the one moment there is still someone to tell.
    /// </summary>
    [Test]
    public void AStringMapStoringAnEntryUnderTheTypeNameIsRefusedByEitherSerializer()
    {
        JobDataMap original = new JobDataMap { { "dictionary", new Dictionary<string, string> { ["$type"] = "mine" } } };

        foreach ((string label, IObjectSerializer writer, string _) in Writers())
        {
            Action write = () => writer.Serialize(original);

            write.Should().Throw<JsonSerializationException>(
                    "{0}: a map that cannot be read back is refused while the application that wrote it can still hear about it", label)
                .Which.Message.Should().Contain("dictionary", "the failure has to say which entry it is about")
                .And.Contain("$type", "and which name inside it is the problem");
        }
    }

    /// <summary>
    /// The values neither serializer will write, because neither could read them back. Each of these
    /// used to serialize without complaint and throw on the way out, by which time the blob was in the
    /// database and the failure belonged to whoever next ran the job.
    /// </summary>
    /// <remarks>
    /// The time zone is the one Newtonsoft brought: Json.NET wrote it as its whole public surface, of
    /// which every member is read-only, so the payload it produced could not be read by anything —
    /// itself included.
    /// </remarks>
    private static IEnumerable<TestCaseData> UnreadableValues()
    {
        yield return Refused("list", new List<string> { "a", "b" }, "System.Collections.Generic.List");
        yield return Refused("dictionaryOfObject", new Dictionary<string, object> { ["inner"] = 1 }, "System.Collections.Generic.Dictionary");
        yield return Refused("nested", new JobDataMap { { "inner", "value" } }, "Quartz.JobDataMap");
        yield return Refused("zone", TimeZones.FindById("Tokyo Standard Time"), "System.TimeZoneInfo");
    }

    private static TestCaseData Refused(string key, object value, string expectedTypeName)
    {
        return new TestCaseData(key, value, expectedTypeName).SetName("{m}(" + key + ")");
    }

    [TestCaseSource(nameof(UnreadableValues))]
    public void AValueTheReaderCannotAcceptIsRefusedOnWriteByEitherSerializer(string key, object value, string expectedTypeName)
    {
        JobDataMap original = new JobDataMap { { key, value } };

        foreach ((string label, IObjectSerializer writer, string declaration) in Writers())
        {
            Action write = () => writer.Serialize(original);

            write.Should().Throw<JsonSerializationException>(
                    "{0}: a value written now and unreadable later is a blob in the database nobody can load, and the write is the last moment anyone can be told", label)
                .Which.Message.Should().Contain(key, "the failure has to say which entry it is about")
                .And.Contain(expectedTypeName, "and what was in it")
                .And.Contain(declaration, "and how an application declares a type of its own");
        }
    }

    /// <summary>
    /// Refusing on write must not close the door an application is told to use. A type declared through
    /// <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" /> is written, and reading it
    /// back gives what the store format gives for any object: a
    /// <c>Dictionary&lt;string, string&gt;</c>.
    /// </summary>
    [Test]
    public void ATypeTheApplicationDeclaredIsWrittenAndRead()
    {
        JobDataMap original = new JobDataMap { { "report", new ApplicationJobDataValue { Name = "monthly" } } };

        Action withoutDeclaration = () => systemTextJsonSerializer.Serialize(original);
        withoutDeclaration.Should().Throw<JsonSerializationException>(
            "an application type Quartz has not been told about is exactly the case the refusal exists for");

        SystemTextJsonSerializerRegistry registry = new();
        registry.AddTypeInfoResolver(ApplicationJobDataContext.Default);
        IObjectSerializer declared = new SystemTextJsonObjectSerializer(registry);

        JobDataMap restored = declared.Deserialize<JobDataMap>(declared.Serialize(original))!;

        restored["report"].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "monthly" },
            "declaring a type is what lets it be written; the store format still hands an object back as a string map");
    }

    /// <summary>
    /// Refusing on write must not close the door this package tells an application to use. A type
    /// declared through <see cref="NewtonsoftJsonSerializerRegistry.AddJobDataValueType{T}" /> is
    /// written, and Json.NET — which carries a <c>$type</c> and constructs from it — hands the type
    /// itself back.
    /// </summary>
    /// <remarks>
    /// That is where the two escape hatches differ, and deliberately: what a declared type comes back as
    /// is each reader's own answer, so a value that has to survive a change of serializer belongs in a
    /// string the job writes itself. The declaration is what the serializers agree on.
    /// </remarks>
    [Test]
    public void ATypeTheApplicationDeclaredIsWrittenAndReadByNewtonsoftToo()
    {
        JobDataMap original = new JobDataMap { { "report", new ApplicationJobDataValue { Name = "monthly" } } };

        Action withoutDeclaration = () => newtonsoftSerializer.Serialize(original);
        withoutDeclaration.Should().Throw<JsonSerializationException>(
            "an application type Quartz has not been told about is exactly the case the refusal exists for");

        NewtonsoftJsonSerializerRegistry registry = new();
        registry.AddJobDataValueType<ApplicationJobDataValue>();
        IObjectSerializer declared = new NewtonsoftJsonObjectSerializer(registry);

        JobDataMap restored = declared.Deserialize<JobDataMap>(declared.Serialize(original))!;

        restored["report"].Should().BeOfType<ApplicationJobDataValue>(
                "Json.NET writes the type name beside the value and builds the type back out of it")
            .Which.Name.Should().Be("monthly");
    }

    /// <summary>
    /// The Newtonsoft reader loads a job data value's object without parsing dates, so that a string map's
    /// entries come back as the strings that went into them. A declared type's own members are unaffected:
    /// what turns text into a <see cref="DateTimeOffset" /> there is the member's type, not the reader's
    /// guess at what the text looks like.
    /// </summary>
    [Test]
    public void ATypeTheApplicationDeclaredStillReadsItsOwnDateBack()
    {
        NewtonsoftJsonSerializerRegistry registry = new();
        registry.AddJobDataValueType<ApplicationSchedule>();
        IObjectSerializer declared = new NewtonsoftJsonObjectSerializer(registry);

        DateTimeOffset due = new(1982, 6, 28, 1, 1, 1, TimeSpan.FromHours(3));
        JobDataMap original = new JobDataMap { { "schedule", new ApplicationSchedule { DueUtc = due } } };

        JobDataMap restored = declared.Deserialize<JobDataMap>(declared.Serialize(original))!;

        restored["schedule"].Should().BeOfType<ApplicationSchedule>().Which.DueUtc.Should().Be(due);
    }

    /// <summary>
    /// Nesting is where the two formats stopped agreeing, so this is the guarantee an application gets:
    /// none. Neither reader hands a nested <see cref="JobDataMap" /> back — an object with no type beside
    /// it is a string map to both of them — which is why both now refuse to write one, and why a blob that
    /// already holds one still has to be read for what it is. Job code that needs structure has to
    /// serialize the structure itself and store the result as a string.
    /// </summary>
    /// <remarks>
    /// The payload is what the Newtonsoft writer produced for a nested map before it was refused: the
    /// map's converter owns its own output, so the nested map carried no <c>$type</c> and there was
    /// never anything to rebuild it from.
    /// </remarks>
    [Test]
    public void ABlobThatAlreadyHoldsANestedMapReadsBackAsSomethingElse()
    {
        byte[] written = Encoding.UTF8.GetBytes("""{"nested":{"inner":"value"}}""");

        foreach ((string label, IObjectSerializer reader) in Readers())
        {
            JobDataMap restored = reader.Deserialize<JobDataMap>(written)!;

            restored["nested"].Should().NotBeOfType<JobDataMap>(
                    "{0}: neither format carries a nested map's identity, so job code must not expect one back", label)
                .And.BeOfType<Dictionary<string, string>>(
                    "{0}: what it is instead is the one object shape the store format has, and both readers say the same thing about it", label);
        }
    }

    private IEnumerable<(string Label, IObjectSerializer Writer, IObjectSerializer Reader)> Pairings()
    {
        yield return ("newtonsoft -> newtonsoft", newtonsoftSerializer, newtonsoftSerializer);
        yield return ("newtonsoft -> system.text.json", newtonsoftSerializer, systemTextJsonSerializer);
        yield return ("system.text.json -> newtonsoft", systemTextJsonSerializer, newtonsoftSerializer);
        yield return ("system.text.json -> system.text.json", systemTextJsonSerializer, systemTextJsonSerializer);
    }

    /// <summary>
    /// Each writer beside the registration its refusal points an application at, since that name is
    /// half of what makes the message useful.
    /// </summary>
    private IEnumerable<(string Label, IObjectSerializer Writer, string Declaration)> Writers()
    {
        yield return ("newtonsoft", newtonsoftSerializer, "AddJobDataValueType");
        yield return ("system.text.json", systemTextJsonSerializer, "AddTypeInfoResolver");
    }

    /// <summary>
    /// Both readers, for the payloads that are already stored: whichever wrote a blob, either has to be
    /// able to load it.
    /// </summary>
    private IEnumerable<(string Label, IObjectSerializer Reader)> Readers()
    {
        yield return ("newtonsoft", newtonsoftSerializer);
        yield return ("system.text.json", systemTextJsonSerializer);
    }
}

/// <summary>A job data value type of the application's own, which no contract of Quartz's can name.</summary>
public sealed class ApplicationJobDataValue
{
    public string Name { get; set; }
}

/// <summary>
/// A declared job data value type with a date in it, for the members whose conversion belongs to the
/// member rather than to whatever the reader makes of the text.
/// </summary>
public sealed class ApplicationSchedule
{
    public DateTimeOffset DueUtc { get; set; }
}

/// <summary>
/// The metadata an application hands to <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />
/// so that Quartz will write a job data value of its own.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ApplicationJobDataValue))]
internal sealed partial class ApplicationJobDataContext : JsonSerializerContext;
