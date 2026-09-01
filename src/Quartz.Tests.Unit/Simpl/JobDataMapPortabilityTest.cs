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

    private IObjectSerializer SerializerNamed(string name)
    {
        return name == "newtonsoft" ? newtonsoftSerializer : systemTextJsonSerializer;
    }
}
