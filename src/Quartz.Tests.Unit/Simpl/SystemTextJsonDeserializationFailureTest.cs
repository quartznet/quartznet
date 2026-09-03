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
using System.Text;

using Quartz.Impl.Calendar;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A job store blob that <see cref="SystemTextJsonObjectSerializer" /> cannot read has to fail as
/// <see cref="Quartz.JsonSerializationException" />, carrying the payload that could not be read and
/// the underlying parse failure, whether the failure came from one of Quartz's converters or from the
/// reader itself. That is the exception callers catch and the dashboard special-cases.
/// </summary>
public class SystemTextJsonDeserializationFailureTest
{
    private SystemTextJsonObjectSerializer serializer;

    /// <summary>
    /// Valid up to the point it stops - the shape a truncated write leaves behind.
    /// </summary>
    private const string TruncatedCalendar = "{\"$type\": \"Quartz.Impl.Calendar.AnnualCalendar, Quartz\", \"Description\": \"Test AnnualCalendar";

    /// <summary>
    /// Parses perfectly and means nothing - the shape a blob written by something else has.
    /// </summary>
    private const string WellFormedButNotATrigger = "{\"totally\": \"valid json\", \"just\": [\"not\", \"a\", \"trigger\"]}";

    [SetUp]
    public void SetUp()
    {
        serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();
    }

    [Test]
    public void SyntacticallyBrokenPayloadFailsAsQuartzJsonSerializationException()
    {
        Action act = () => serializer.DeSerialize<AnnualCalendar>(Encoding.UTF8.GetBytes(TruncatedCalendar));

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

        Action act = () => serializer.DeSerialize<AnnualCalendar>(Encoding.UTF8.GetBytes(NotJson));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>(
                "a payload rejected on its very first token never reaches a converter, and must still fail the same way")
            .Which;

        thrown.Message.Should().Contain(NotJson);
        thrown.InnerException.Should().BeOfType<System.Text.Json.JsonException>(
            "the reader's own refusal is what says where the payload broke");
    }

    [Test]
    public void WellFormedButWrongShapePayloadFailsAsQuartzJsonSerializationException()
    {
        Action act = () => serializer.DeSerialize<IOperableTrigger>(Encoding.UTF8.GetBytes(WellFormedButNotATrigger));

        Quartz.JsonSerializationException thrown = act.Should().Throw<Quartz.JsonSerializationException>().Which;

        thrown.Should().BeAssignableTo<SchedulerException>();
        thrown.Message.Should().Contain(WellFormedButNotATrigger);
        thrown.InnerException.Should().NotBeNull();
    }
}
