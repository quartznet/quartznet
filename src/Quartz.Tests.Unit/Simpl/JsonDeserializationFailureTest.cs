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
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A job store blob that cannot be read has to fail the same way whichever serializer is reading
/// it: as <see cref="Quartz.JsonSerializationException" />, carrying the payload that could not be
/// read and the underlying parse failure. That is the exception callers catch, the HTTP API's
/// exception handler maps, and the dashboard special-cases.
/// </summary>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class JsonDeserializationFailureTest
{
    private readonly IObjectSerializer serializer;

    public JsonDeserializationFailureTest(Type serializerType)
    {
        serializer = (IObjectSerializer) Activator.CreateInstance(serializerType)!;

        if (serializer is NewtonsoftJsonObjectSerializer newtonsoft)
        {
            // Newtonsoft only understands the trigger wire format when its converter is registered.
            newtonsoft.RegisterTriggerConverters = true;
        }
    }

    /// <summary>
    /// Valid up to the point it stops - the shape a truncated write leaves behind.
    /// </summary>
    private const string TruncatedCalendar =
        """{"$type": "Quartz.Impl.Calendar.AnnualCalendar, Quartz", "Description": "Test AnnualCalendar""";

    /// <summary>
    /// Parses perfectly and means nothing - the shape a blob written by something else has.
    /// </summary>
    private const string WellFormedButNotATrigger =
        """{"totally": "valid json", "just": ["not", "a", "trigger"]}""";

    [Test]
    public void SyntacticallyBrokenPayloadFailsAsQuartzJsonSerializationException()
    {
        Action act = () => serializer.Deserialize<AnnualCalendar>(Encoding.UTF8.GetBytes(TruncatedCalendar));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>(
                "a parse failure must not escape as the underlying library's own exception type")
            .Which;

        thrown.Should().BeAssignableTo<SchedulerException>();
        thrown.Message.Should().Contain(TruncatedCalendar, "the payload that could not be read is the whole diagnostic");
        thrown.InnerException.Should().NotBeNull("the underlying parse failure is what says where the payload broke");
    }

    [Test]
    public void PayloadThatIsNotJsonAtAllFailsAsQuartzJsonSerializationException()
    {
        const string NotJson = "this is not json";

        Action act = () => serializer.Deserialize<AnnualCalendar>(Encoding.UTF8.GetBytes(NotJson));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>(
                "a payload rejected on its very first token never reaches a converter, and must still fail the same way")
            .Which;

        thrown.Message.Should().Contain(NotJson);
        thrown.InnerException.Should().NotBeNull();
    }

    [Test]
    public void WellFormedButWrongShapePayloadFailsAsQuartzJsonSerializationException()
    {
        Action act = () => serializer.Deserialize<IOperableTrigger>(Encoding.UTF8.GetBytes(WellFormedButNotATrigger));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>().Which;

        thrown.Should().BeAssignableTo<SchedulerException>();
        thrown.Message.Should().Contain(WellFormedButNotATrigger);
        thrown.InnerException.Should().NotBeNull();
    }
}

/// <summary>
/// <c>Quartz.JsonSerializationException</c> shadows <c>Newtonsoft.Json.JsonSerializationException</c>
/// throughout this package - every file in it sits under the <c>Quartz</c> namespace, and the
/// enclosing namespace beats a <c>using</c> - so an unqualified <c>catch</c> that reads as if it
/// caught Newtonsoft's parse failures caught nothing of the sort, and they escaped raw.
/// </summary>
public class NewtonsoftJsonDeserializationFailureTest
{
    [Test]
    public void ReaderFailureIsWrappedRatherThanEscapingAsNewtonsoftsOwnException()
    {
        NewtonsoftJsonObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();

        // The calendar converter has no catch of its own, so Newtonsoft's reader exception travels
        // all the way out to the serializer - the exact path the shadowed catch used to miss.
        const string Truncated = """{"$type": "Quartz.Impl.Calendar.AnnualCalendar, Quartz", "Description": "x""";

        Action act = () => serializer.Deserialize<AnnualCalendar>(Encoding.UTF8.GetBytes(Truncated));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>().Which;

        thrown.Message.Should().Contain(Truncated);
        thrown.InnerException.Should().BeOfType<global::Newtonsoft.Json.JsonReaderException>();
    }

    [Test]
    public void NewtonsoftSerializationFailureIsWrappedRatherThanEscaping()
    {
        // No trigger converter registered, so Newtonsoft's own contract handling reads the payload
        // and rejects the array with its JsonSerializationException.
        NewtonsoftJsonObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();

        const string Array = "[1, 2, 3]";

        Action act = () => serializer.Deserialize<SimpleTriggerImpl>(Encoding.UTF8.GetBytes(Array));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>().Which;

        thrown.Message.Should().Contain(Array);
        thrown.InnerException.Should().BeOfType<global::Newtonsoft.Json.JsonSerializationException>();
    }

    [Test]
    public void ConverterFailureStillSurfacesAsQuartzsExceptionWithThePayload()
    {
        // The trigger converter already throws Quartz's exception; wrapping it is what attaches the
        // payload, and that has to keep working now that the catch names three types instead of one.
        NewtonsoftJsonObjectSerializer serializer = new NewtonsoftJsonObjectSerializer { RegisterTriggerConverters = true };

        const string NotATrigger = """{"totally": "valid json"}""";

        Action act = () => serializer.Deserialize<IOperableTrigger>(Encoding.UTF8.GetBytes(NotATrigger));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>().Which;

        thrown.Message.Should().Contain(NotATrigger);
        thrown.InnerException.Should().BeOfType<Quartz.JsonSerializationException>()
            .Which.Message.Should().Be("Failed to parse ITrigger from json");
    }
}
