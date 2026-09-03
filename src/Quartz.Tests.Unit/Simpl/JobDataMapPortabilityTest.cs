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

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A job data blob written by one serializer has to be readable by the other. Both are documented
/// store formats and an application is free to switch between them, so a column's contents outlive
/// the choice that wrote them.
/// </summary>
/// <remarks>
/// <para>
/// A <c>Dictionary&lt;string, string&gt;</c> is the one job data value with structure that both
/// formats carry, and it is the entry the two disagreed about (#3582). Json.NET names the type of any
/// value an <see cref="object" />-typed slot cannot, so <c>Quartz.Serialization.Json</c> writes the map
/// with a <c>$type</c> beside it; <c>Quartz.Serialization.SystemTextJson</c> writes the plain object and
/// its reader takes every property of an object as an entry of the map. So a map written by one and
/// read by the other came back with an assembly-qualified type name in the application's own key space,
/// and the crossing failed the other way too: a plain object is a shape Json.NET has no answer for, so
/// a map the built-in serializer wrote came back as a <see cref="JObject" /> the job that stored it
/// cannot cast.
/// </para>
/// <para>
/// Both readers were fixed and neither writer was: this is a released branch and what it writes is
/// what other 3.x nodes read. <see cref="TheNewtonsoftWriterStillNamesTheTypeBesideAStringMap" /> is
/// what says so.
/// </para>
/// </remarks>
public class JobDataMapPortabilityTest
{
    private static readonly Dictionary<string, string> stringMap = new Dictionary<string, string>
    {
        ["one"] = "1",
        // Not decoration: Json.NET parses a string that looks like a timestamp into a DateTimeOffset
        // wherever it reads a value, and rendering that back as a string reformats it - in the reading
        // machine's culture, and with the reading machine's offset when the text carried none. A string
        // map has to hand back the strings that went into it.
        ["when"] = "1982-06-28T01:01:01+03:00"
    };

    private IObjectSerializer newtonsoftSerializer;
    private IObjectSerializer systemTextJsonSerializer;

    [SetUp]
    public void SetUp()
    {
        JsonObjectSerializer newtonsoft = new JsonObjectSerializer();
        newtonsoft.Initialize();
        newtonsoftSerializer = newtonsoft;

        SystemTextJsonObjectSerializer systemTextJson = new SystemTextJsonObjectSerializer();
        systemTextJson.Initialize();
        systemTextJsonSerializer = systemTextJson;
    }

    [TestCase("newtonsoft", "newtonsoft")]
    [TestCase("newtonsoft", "system.text.json")]
    [TestCase("system.text.json", "newtonsoft")]
    [TestCase("system.text.json", "system.text.json")]
    public void AStringMapReadsBackAsItselfWhicheverSerializerWroteIt(string writerName, string readerName)
    {
        IObjectSerializer writer = SerializerNamed(writerName);
        IObjectSerializer reader = SerializerNamed(readerName);

        JobDataMap original = new JobDataMap { { "dictionary", new Dictionary<string, string>(stringMap) } };

        JobDataMap restored = reader.DeSerialize<JobDataMap>(writer.Serialize(original));

        restored.Should().ContainKey("dictionary");
        restored["dictionary"].Should().BeOfType<Dictionary<string, string>>(
                "a string map is the one shape past the scalars the store format carries, and it has to come back as itself whichever serializer wrote it")
            .Which.Should().Equal(stringMap,
                "the entries the application stored are the entries it gets back - no type name added, and no timestamp-shaped value reformatted");
    }

    /// <summary>
    /// The scalars have always crossed, and a read written entry by entry must not change that.
    /// </summary>
    [TestCase("newtonsoft", "newtonsoft")]
    [TestCase("newtonsoft", "system.text.json")]
    [TestCase("system.text.json", "newtonsoft")]
    [TestCase("system.text.json", "system.text.json")]
    public void TheScalarValuesStillCross(string writerName, string readerName)
    {
        IObjectSerializer writer = SerializerNamed(writerName);
        IObjectSerializer reader = SerializerNamed(readerName);

        JobDataMap original = new JobDataMap
        {
            { "string", "value" },
            { "bool", true },
            { "int", 123 },
            { "long", 9_000_000_000L },
            { "double", 12.34 },
            { "null", null }
        };

        JobDataMap restored = reader.DeSerialize<JobDataMap>(writer.Serialize(original));

        restored.GetString("string").Should().Be("value");
        restored.GetBoolean("bool").Should().BeTrue();
        restored.GetInt("int").Should().Be(123);
        restored.GetLong("long").Should().Be(9_000_000_000L);
        restored.GetDouble("double").Should().Be(12.34);
        restored["null"].Should().BeNull();
    }

    /// <summary>
    /// The payloads that are already in databases. Json.NET wrote a string map with the type it had
    /// written it under, and both readers have to hand back the map the application stored, without the
    /// type name in it.
    /// </summary>
    /// <remarks>
    /// The payload is written out here rather than produced by the current writer, so that a writer that
    /// changes cannot change what this asserts. Only the type name itself is taken from the runtime,
    /// because it names <c>System.Private.CoreLib</c> on <c>net10.0</c> and <c>mscorlib</c> on
    /// <c>net472</c> and a literal would read on one framework only.
    /// </remarks>
    [TestCase("newtonsoft")]
    [TestCase("system.text.json")]
    public void ABlobThatNamesTheTypeBesideAStringMapStillReadsBackAsTheMap(string readerName)
    {
        IObjectSerializer reader = SerializerNamed(readerName);
        string typeName = typeof(Dictionary<string, string>).AssemblyQualifiedName;
        byte[] written = Encoding.UTF8.GetBytes(
            "{\"dictionary\":{\"$type\":\"" + typeName + "\",\"one\":\"1\",\"when\":\"1982-06-28T01:01:01+03:00\"}}");

        JobDataMap restored = reader.DeSerialize<JobDataMap>(written);

        restored["dictionary"].Should().BeOfType<Dictionary<string, string>>(
                "an upgrade does not get to make a stored map unreadable")
            .Which.Should().Equal(stringMap,
                "the type Json.NET wrote the map under is metadata, not an entry of the application's");
    }

    /// <summary>
    /// This branch changes no byte it writes. The fix is on both readers alone, because a 3.x node that
    /// has not been upgraded is reading these columns and only the payload it already understands is
    /// safe to hand it.
    /// </summary>
    [Test]
    public void TheNewtonsoftWriterStillNamesTheTypeBesideAStringMap()
    {
        JobDataMap original = new JobDataMap { { "dictionary", new Dictionary<string, string>(stringMap) } };

        JObject written = JObject.Parse(Encoding.UTF8.GetString(newtonsoftSerializer.Serialize(original)));

        written["dictionary"]["$type"].Value<string>().Should().StartWith("System.Collections.Generic.Dictionary",
            "dropping the type name is a wire change on a released branch, and this half of the fix deliberately does not make one");
    }

    /// <summary>
    /// And the built-in serializer still writes the plain object it always wrote, which is the shape the
    /// Newtonsoft reader learned to read.
    /// </summary>
    [Test]
    public void TheSystemTextJsonWriterStillWritesThePlainObject()
    {
        JobDataMap original = new JobDataMap { { "dictionary", new Dictionary<string, string> { ["one"] = "1" } } };

        Encoding.UTF8.GetString(systemTextJsonSerializer.Serialize(original)).Should().Be("{\"dictionary\":{\"one\":\"1\"}}");
    }

    /// <summary>
    /// Structure inside a stored string map is a blob neither reader has an answer for, and the
    /// Newtonsoft reader now says so as a serialization failure rather than letting a cast fail somewhere
    /// further away.
    /// </summary>
    [Test]
    public void StructureNestedInsideAStoredStringMapFailsAsASerializationError()
    {
        byte[] written = Encoding.UTF8.GetBytes("{\"dictionary\":{\"inner\":{\"deeper\":\"1\"}}}");

        Action read = () => newtonsoftSerializer.DeSerialize<JobDataMap>(written);

        read.Should().Throw<Newtonsoft.Json.JsonSerializationException>()
            .WithMessage("*inner*", "the failure has to say which entry it is about");
    }

    /// <summary>
    /// The values System.Text.Json refuses to write, because it could never read them back: a JSON
    /// array, and an object of any shape but the string map the reader produces. Each of these used to
    /// serialize without complaint and throw on the way out, by which time the blob was in the database
    /// and the failure belonged to whoever next ran the job (#3495).
    /// </summary>
    private static IEnumerable<TestCaseData> UnreadableValues()
    {
        yield return Refused("list", new List<string> { "a", "b" }, "System.Collections.Generic.List");
        yield return Refused("dictionaryOfObject", new Dictionary<string, object> { ["inner"] = 1 }, "System.Collections.Generic.Dictionary");
        yield return Refused("nested", new JobDataMap { { "inner", "value" } }, "Quartz.JobDataMap");

        // Written as {"Name":"monthly"}, which is byte for byte a string map - and comes back as one,
        // never as the object that went in. The shape is not what decides; the value's own type is.
        yield return Refused("object", new ApplicationJobDataValue { Name = "monthly" }, "ApplicationJobDataValue");
    }

    private static TestCaseData Refused(string key, object value, string expectedTypeName)
    {
        return new TestCaseData(key, value, expectedTypeName).SetName("{m}(" + key + ")");
    }

    [TestCaseSource(nameof(UnreadableValues))]
    public void AValueTheReaderCannotAcceptIsRefusedOnWrite(string key, object value, string expectedTypeName)
    {
        JobDataMap original = new JobDataMap { { key, value } };

        Action write = () => systemTextJsonSerializer.Serialize(original);

        write.Should().Throw<Quartz.JsonSerializationException>(
                "a value written now and unreadable later is a blob in the database nobody can load, and the write is the last moment anyone can be told")
            .Which.Message.Should().Contain(key, "the failure has to say which entry it is about")
            .And.Contain(expectedTypeName, "and what was in it")
            .And.Contain("CreateSerializerOptions", "and how an application declares a type of its own");
    }

    /// <summary>
    /// The values a store has always taken and still takes, none of which is a type the reader names.
    /// Each is a number or a string in the column, so each comes back - as an <c>int</c>, or as the
    /// string the accessors coerce - and refusing them over an upgrade would have made a failing
    /// application out of a working one.
    /// </summary>
    private static IEnumerable<TestCaseData> CoercedValues()
    {
        yield return Coerced("short", (short) 7, 7);
        yield return Coerced("byteArray", new byte[] { 1, 2, 3 }, Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        yield return Coerced("uri", new Uri("https://www.quartz-scheduler.net/"), "https://www.quartz-scheduler.net/");
        yield return Coerced("enum", DayOfWeek.Friday, (int) DayOfWeek.Friday);
    }

    private static TestCaseData Coerced(string key, object value, object expected)
    {
        return new TestCaseData(key, value, expected).SetName("{m}(" + key + ")");
    }

    [TestCaseSource(nameof(CoercedValues))]
    public void AValueTheReaderCoercesSurvivesTheRoundTrip(string key, object value, object expected)
    {
        JobDataMap original = new JobDataMap { { key, value } };

        JobDataMap restored = systemTextJsonSerializer.DeSerialize<JobDataMap>(systemTextJsonSerializer.Serialize(original));

        restored[key].Should().Be(expected,
            "the refusal is of what the reader cannot hand back at all, not of what it hands back as a number or a string");
    }

    /// <summary>
    /// Every value the reader hands back as itself, or as something the accessors coerce, is still
    /// written: the refusal is of what cannot come back, not of what comes back changed.
    /// </summary>
    [Test]
    public void EveryValueTheAccessorsReadIsStillWritten()
    {
        JobDataMap original = new JobDataMap
        {
            { "string", "text" },
            { "bool", true },
            { "char", 'c' },
            { "int", 1 },
            { "long", 2L },
            { "float", 3.5f },
            { "double", 4.5d },
            { "decimal", 5.5m },
            { "dateTime", new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc) },
            { "dateTimeOffset", new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)) },
            { "timeSpan", TimeSpan.FromMinutes(90) },
            { "guid", Guid.NewGuid() },
            { "enum", DayOfWeek.Friday },
            { "stringMap", stringMap },
            { "null", null }
        };

        Action write = () => systemTextJsonSerializer.Serialize(original);

        write.Should().NotThrow("each of these is a type JobDataMap has an accessor for, or the one object shape the reader produces");
    }

    /// <summary>
    /// Refusing on write must not close the door an application is told to use. A type the
    /// application declares a converter for - by overriding
    /// <see cref="SystemTextJsonObjectSerializer.CreateSerializerOptions" /> - is written, and reading it
    /// back gives what the store format gives for any object: a <c>Dictionary&lt;string, string&gt;</c>.
    /// </summary>
    [Test]
    public void ATypeTheApplicationDeclaredIsWrittenAndRead()
    {
        JobDataMap original = new JobDataMap { { "report", new ApplicationJobDataValue { Name = "monthly" } } };

        Action withoutDeclaration = () => systemTextJsonSerializer.Serialize(original);
        withoutDeclaration.Should().Throw<Quartz.JsonSerializationException>(
            "an application type Quartz has not been told about is exactly the case the refusal exists for");

        DeclaringSerializer declared = new DeclaringSerializer();
        declared.Initialize();

        JobDataMap restored = declared.DeSerialize<JobDataMap>(declared.Serialize(original));

        restored["report"].Should().BeEquivalentTo(new Dictionary<string, string> { ["Name"] = "monthly" },
            "declaring a type is what lets it be written; the store format still hands an object back as a string map");
    }

    private IObjectSerializer SerializerNamed(string name)
    {
        return name == "newtonsoft" ? newtonsoftSerializer : systemTextJsonSerializer;
    }

    /// <summary>A job data value type of the application's own, which no contract of Quartz's can name.</summary>
    public sealed class ApplicationJobDataValue
    {
        public string Name { get; set; }
    }

    /// <summary>
    /// The serializer an application configures when it has a job data value type of its own: the
    /// converter it adds is its declaration that the type can be written.
    /// </summary>
    private sealed class DeclaringSerializer : SystemTextJsonObjectSerializer
    {
        protected override System.Text.Json.JsonSerializerOptions CreateSerializerOptions()
        {
            System.Text.Json.JsonSerializerOptions options = base.CreateSerializerOptions();
            options.Converters.Add(new ApplicationJobDataValueConverter());
            return options;
        }
    }

    private sealed class ApplicationJobDataValueConverter : System.Text.Json.Serialization.JsonConverter<ApplicationJobDataValue>
    {
        public override ApplicationJobDataValue Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            throw new NotSupportedException("the store format reads an object back as a string map, never as the type that went in");
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, ApplicationJobDataValue value, System.Text.Json.JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", value.Name);
            writer.WriteEndObject();
        }
    }
}
