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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Serialization.Newtonsoft;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="JobKey" /> and <see cref="TriggerKey" /> are immutable and have no parameterless
/// constructor, so both serializers bind them through a converter rather than by populating properties.
/// These pin that a key survives a round trip and that a payload written before the keys became
/// immutable — the same name and group properties, which is all a key ever wrote — still reads.
/// </summary>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class KeySerializationTest
{
    private readonly IObjectSerializer serializer;

    public KeySerializationTest(Type serializerType)
    {
        serializer = (IObjectSerializer) Activator.CreateInstance(serializerType);
    }

    [Test]
    public void JobKeyRoundTrips()
    {
        JobKey key = new("myJob", "reports");

        JobKey deserialized = serializer.Deserialize<JobKey>(serializer.Serialize(key));

        deserialized.Should().Be(key);
    }

    [Test]
    public void TriggerKeyRoundTrips()
    {
        TriggerKey key = new("myTrigger", "reports");

        TriggerKey deserialized = serializer.Deserialize<TriggerKey>(serializer.Serialize(key));

        deserialized.Should().Be(key);
    }

    [Test]
    public void ReadsAPayloadWrittenBeforeTheKeysBecameImmutable()
    {
        // What both serializers wrote when a key was a mutable class bound by its properties.
        byte[] legacy = Encoding.UTF8.GetBytes(
            """{"$type":"Quartz.JobKey, Quartz","Name":"myJob","Group":"reports"}""");

        JobKey deserialized = serializer.Deserialize<JobKey>(legacy);

        deserialized.Should().Be(new JobKey("myJob", "reports"));
    }
}

/// <summary>
/// The Newtonsoft serializer resolves a value's type from its <c>$type</c> property, so it can read a key
/// back out of a slot typed as <see cref="object" /> — a job data map entry. A converter would not be
/// consulted on that path, which is why the key constructor is named through the contract resolver instead.
/// The System.Text.Json serializer does not do polymorphic values in a job data map at all, for keys or for
/// anything else, so there is nothing to assert for it here.
/// </summary>
/// <remarks>
/// A key is not one of the value types a job data map declares an accessor for, so it takes a declaration
/// to write one — the same declaration any type of the application's own takes, and for the same reason:
/// the other serializer hands that blob back as a string map rather than as a key, so the value belongs to
/// whichever serializer wrote it. What is asserted here is that a declared key still travels intact, since
/// the contract resolver's work is what makes it possible at all.
/// </remarks>
public class NewtonsoftKeySerializationTest
{
    [Test]
    public void KeyInAJobDataMapIsRefusedUntilItIsDeclared()
    {
        NewtonsoftJsonObjectSerializer serializer = new();
        JobDataMap map = new() { ["job"] = new JobKey("myJob", "reports") };

        Action write = () => serializer.Serialize(map);

        write.Should().Throw<JsonSerializationException>(
                "a key is not a job data value either store format promises to give back, so it is refused like any other type of the application's own")
            .Which.Message.Should().Contain("job", "the failure has to say which entry it is about")
            .And.Contain("Quartz.JobKey", "and what was in it");
    }

    [Test]
    public void ADeclaredKeyInAJobDataMapKeepsItsType()
    {
        NewtonsoftJsonSerializerRegistry registry = new();
        registry.AddJobDataValueType<JobKey>();
        NewtonsoftJsonObjectSerializer serializer = new(registry);
        JobDataMap map = new() { ["job"] = new JobKey("myJob", "reports") };

        JobDataMap deserialized = serializer.Deserialize<JobDataMap>(serializer.Serialize(map));

        deserialized.Should().NotBeNull();
        deserialized["job"].Should().Be(new JobKey("myJob", "reports"),
            "the contract resolver names the key's constructor on the $type path, which is what a converter could not reach");
    }
}
