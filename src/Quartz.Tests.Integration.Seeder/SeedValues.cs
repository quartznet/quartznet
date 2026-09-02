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

using System.Globalization;

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// The job data map 3.20 writes, and the manifest description of it, from one declaration.
/// </summary>
/// <remarks>
/// <para>
/// The set is every type 4.0's JSON write gate admits — <c>JobDataValues.Accepted</c> plus enums —
/// because those are the values an application can still store after upgrading, so those are the ones
/// whose 3.x-written blobs have to keep reading. The <c>Dictionary&lt;string, string&gt;</c> is there
/// for its own reason: it is the #3582 shape, the one 3.x's Newtonsoft writer decorates with
/// <c>$type</c> and 4.0's does not.
/// </para>
/// <para>
/// A <see cref="JobKey" /> value is <em>outside</em> the gate — 4.0 would refuse to write one — and it
/// is seeded on a job of its own for that reason. A 3.x database can hold one, so what 4.0 makes of it
/// on the way back out is a fair question; putting it on the job every trigger points at would let one
/// unreadable entry take the whole rehearsal down with it.
/// </para>
/// </remarks>
internal static class SeedValues
{
    internal static readonly Guid Id = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly DateTime Moment = new DateTime(2024, 7, 1, 3, 30, 0, DateTimeKind.Utc);

    private static readonly Entry[] Entries =
    [
        new Entry("text", "string", "staging"),
        new Entry("flag", "bool", true),
        new Entry("count", "int", 42),
        new Entry("big", "long", 9_000_000_000L),
        new Entry("ratio", "double", 2.5d),
        new Entry("small", "float", 1.5f),
        new Entry("money", "decimal", 12.34m),
        new Entry("letter", "char", 'q'),
        new Entry("moment", "dateTime", Moment),
        new Entry("offsetMoment", "dateTimeOffset", new DateTimeOffset(Moment)),
        new Entry("span", "timeSpan", TimeSpan.FromSeconds(42)),
        new Entry("id", "guid", Id),
        new Entry("day", "dateOnly", new DateOnly(2024, 7, 1)),
        new Entry("timeOfDay", "timeOnly", new TimeOnly(3, 30, 0)),
        new Entry("weekday", "enum", DayOfWeek.Friday),
        new Entry("labels", "dictionary", new Dictionary<string, string> { ["alpha"] = "1", ["beta"] = "2" })
    ];

    public static JobDataMap Build()
    {
        JobDataMap map = new JobDataMap();

        foreach (Entry entry in Entries)
        {
            map[entry.Key] = entry.Value;
        }

        return map;
    }

    public static List<SeededDataValue> Describe()
    {
        return Entries.Select(e => new SeededDataValue
        {
            Key = e.Key,
            Kind = e.Kind,
            Text = e.Value is Dictionary<string, string> ? null : Text(e.Value),
            Entries = e.Value as Dictionary<string, string>
        }).ToList();
    }

    /// <summary>The one value 4.0's write gate would refuse, kept apart from the rest.</summary>
    public static JobDataMap BuildOutsideTheWriteGate()
    {
        return new JobDataMap
        {
            ["jobKey"] = new JobKey(LegacySeeder.WorkerJobName, LegacySeeder.JobGroup)
        };
    }

    public static List<SeededDataValue> DescribeOutsideTheWriteGate()
    {
        return
        [
            new SeededDataValue
            {
                Key = "jobKey",
                Kind = "outsideTheWriteGate",
                Text = LegacySeeder.JobGroup + "." + LegacySeeder.WorkerJobName
            }
        ];
    }

    private static string Text(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        char letter => letter.ToString(),
        DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        Guid id => id.ToString("D", CultureInfo.InvariantCulture),
        DateOnly day => day.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        Enum member => member.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private sealed record Entry(string Key, string Kind, object Value);
}
