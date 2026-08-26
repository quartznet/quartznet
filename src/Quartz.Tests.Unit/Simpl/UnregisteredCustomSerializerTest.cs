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
using Quartz.Serialization.Newtonsoft;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A registry is per scheduler, so a blob can outlive the registration that wrote it: a node started
/// without the <c>AddTriggerSerializer</c> call its neighbour has, or a store carried to an
/// application that dropped the custom type. What that node reads is a payload naming a serializer it
/// does not have, and it has to say so — as <see cref="Quartz.JsonSerializationException" />, the
/// exception the HTTP API's handler maps and the dashboard special-cases, with the discriminator it
/// could not resolve in the message. Anything else surfaces as an unhandled failure from inside a
/// converter, which says nothing about which registration is missing.
/// </summary>
[TestFixture]
public class UnregisteredCustomSerializerTest
{
    private static IEnumerable<TestCaseData> Serializers()
    {
        yield return new TestCaseData(SerializerKind.Newtonsoft).SetName("{m}(newtonsoft)");
        yield return new TestCaseData(SerializerKind.SystemTextJson).SetName("{m}(system.text.json)");
    }

    public enum SerializerKind
    {
        Newtonsoft,
        SystemTextJson
    }

    [TestCaseSource(nameof(Serializers))]
    public void ACustomTriggerReadWithoutItsSerializerNamesTheDiscriminator(SerializerKind kind)
    {
        (IObjectSerializer writer, IObjectSerializer reader) = Pair(kind);

        JsonSerializationTestTrigger trigger = new JsonSerializationTestTrigger
        {
            Key = new TriggerKey("custom", "group"),
            JobKey = new JobKey("job", "jobGroup"),
            RepeatInterval = TimeSpan.FromMinutes(5),
            RepeatCount = 3,
            StartTimeUtc = new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero),
            CustomProperty = 42
        };

        byte[] blob = writer.Serialize<IOperableTrigger>(trigger);

        Action act = () => reader.Deserialize<IOperableTrigger>(blob);

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>(
                "a missing registration is a store read failure like any other, not a leak from inside a converter")
            .Which;

        thrown.Should().BeAssignableTo<SchedulerException>();
        thrown.GetBaseException().Message.Should().Contain(
            JsonSerializationTestTrigger.Discriminator,
            "the discriminator is the only thing that says which AddTriggerSerializer call is missing");
    }

    [TestCaseSource(nameof(Serializers))]
    public void ACustomCalendarReadWithoutItsSerializerNamesTheDiscriminator(SerializerKind kind)
    {
        (IObjectSerializer writer, IObjectSerializer reader) = Pair(kind);

        JsonSerializationTestCalendar calendar = new JsonSerializationTestCalendar
        {
            Description = "Custom calendar",
            CustomProperty = 42,
            TimeZone = TimeZoneInfo.Utc
        };

        byte[] blob = writer.Serialize<ICalendar>(calendar);

        Action act = () => reader.Deserialize<ICalendar>(blob);

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>(
                "both serializers write a calendar under its assembly-qualified name, and both have to fail the same way when nothing answers to it")
            .Which;

        thrown.Should().BeAssignableTo<SchedulerException>();
        thrown.GetBaseException().Message.Should().Contain(
            nameof(JsonSerializationTestCalendar),
            "the discriminator is the only thing that says which AddCalendarSerializer call is missing");
    }

    /// <summary>
    /// The write side has the same hole, and it is the one a node hits first: a scheduler that stores
    /// a calendar it has no serializer for should say which one, not fail from inside a converter.
    /// </summary>
    [TestCaseSource(nameof(Serializers))]
    public void ACustomCalendarWrittenWithoutItsSerializerNamesTheDiscriminator(SerializerKind kind)
    {
        (_, IObjectSerializer writerWithoutTheCustomTypes) = Pair(kind);

        JsonSerializationTestCalendar calendar = new JsonSerializationTestCalendar
        {
            Description = "Custom calendar",
            CustomProperty = 42,
            TimeZone = TimeZoneInfo.Utc
        };

        Action act = () => writerWithoutTheCustomTypes.Serialize<ICalendar>(calendar);

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>().Which;

        thrown.GetBaseException().Message.Should().Contain(nameof(JsonSerializationTestCalendar));
    }

    /// <summary>
    /// A writer that knows the custom types, and a reader that knows only the built-ins.
    /// </summary>
    private static (IObjectSerializer Writer, IObjectSerializer Reader) Pair(SerializerKind kind)
    {
        if (kind == SerializerKind.Newtonsoft)
        {
            NewtonsoftJsonSerializerRegistry registry = new NewtonsoftJsonSerializerRegistry()
                .AddTriggerSerializer<JsonSerializationTestTrigger>(new JsonSerializationTestTrigger.NewtonsoftSerializer())
                .AddCalendarSerializer<JsonSerializationTestCalendar>(new JsonSerializationTestCalendar.NewtonsoftSerializer());

            return (
                new NewtonsoftJsonObjectSerializer(registry) { RegisterTriggerConverters = true },
                new NewtonsoftJsonObjectSerializer(new NewtonsoftJsonSerializerRegistry()) { RegisterTriggerConverters = true });
        }

        SystemTextJsonSerializerRegistry systemTextJsonRegistry = new SystemTextJsonSerializerRegistry()
            .AddTriggerSerializer<JsonSerializationTestTrigger>(new JsonSerializationTestTrigger.SystemTextJsonSerializer())
            .AddCalendarSerializer<JsonSerializationTestCalendar>(new JsonSerializationTestCalendar.SystemTextJsonSerializer());

        return (
            new SystemTextJsonObjectSerializer(systemTextJsonRegistry),
            new SystemTextJsonObjectSerializer(new SystemTextJsonSerializerRegistry()));
    }
}
