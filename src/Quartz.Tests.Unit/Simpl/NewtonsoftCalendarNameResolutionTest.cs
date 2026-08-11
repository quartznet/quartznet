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

using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// The Newtonsoft registry indexes a calendar serializer under both the calendar's
/// assembly-qualified type name — what 3.x payloads carry — and the serializer-neutral
/// discriminator, case-insensitively, matching the System.Text.Json package.
/// </summary>
public class NewtonsoftCalendarNameResolutionTest
{
    private const string AssemblyQualifiedName = "Quartz.Impl.Calendar.AnnualCalendar, Quartz";

    [Test]
    public void CalendarResolvesByAssemblyQualifiedName()
    {
        RoundTripWithTypeName(AssemblyQualifiedName).Should().BeOfType<AnnualCalendar>(
            "the key 3.x payloads carry must always stay registered");
    }

    [Test]
    public void CalendarResolvesByDiscriminator()
    {
        RoundTripWithTypeName("AnnualCalendar").Should().BeOfType<AnnualCalendar>(
            "the serializer-neutral name the System.Text.Json package writes resolves here too");
    }

    [Test]
    public void CalendarNameMatchingIsCaseInsensitive()
    {
        RoundTripWithTypeName(AssemblyQualifiedName.ToUpperInvariant()).Should().BeOfType<AnnualCalendar>();
        RoundTripWithTypeName("annualcalendar").Should().BeOfType<AnnualCalendar>();
    }

    private static ICalendar RoundTripWithTypeName(string typeName)
    {
        NewtonsoftJsonObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();

        byte[] data = serializer.Serialize<ICalendar>(new AnnualCalendar());
        string json = Encoding.UTF8.GetString(data);
        json.Should().Contain(AssemblyQualifiedName, "the payload writes the assembly-qualified name, unchanged from 3.x");

        byte[] modified = Encoding.UTF8.GetBytes(json.Replace(AssemblyQualifiedName, typeName));
        return serializer.Deserialize<ICalendar>(modified);
    }
}
