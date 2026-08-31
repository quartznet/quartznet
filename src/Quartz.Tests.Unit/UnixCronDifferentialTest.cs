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

#nullable enable

using NCrontab;

namespace Quartz.Tests.Unit;

/// <summary>
/// Reads the same crontab lines with <see cref="CronFormat.Unix" /> and with NCrontab, and requires
/// them to name the same instants.
/// </summary>
/// <remarks>
/// <para>
/// The rewrite's whole claim is "this string means what crontab says it means", and the only way to
/// test that claim is against something that is not us. NCrontab is a Vixie-faithful five-field
/// parser, so it is the reference here.
/// </para>
/// <para>
/// One thing has to be kept out of the comparison: NCrontab has no union rule - it requires both day
/// fields to agree, where crontab and Quartz fire on either when both name days. Every expression
/// below therefore leaves at least one day field as <c>*</c>, which is the case the two
/// implementations do agree on. The union itself is pinned directly in
/// <see cref="UnixCronFormatTest" />.
/// </para>
/// </remarks>
[TestFixture]
public class UnixCronDifferentialTest
{
    // '5-1' is deliberately absent. Quartz reads a range that wraps the end of the week as a wrap, so
    // 'FRI-MON' is Friday through Monday; NCrontab sorts the endpoints and reads it as Monday through
    // Friday instead. Vixie's own loop sets no bits at all for it. There is no portable answer to
    // compare against, so the wrap is pinned directly in UnixCronFormatTest rather than differentially.
    // '7', '1-7' and '0-7' stay: NCrontab rejects a day-of-week of 7 where crontab has always taken it
    // for Sunday, so those rows skip themselves below and are pinned directly too.
    private static readonly string[] daysOfWeek =
    [
        "0", "1", "5", "6", "7", "1-5", "2-4", "0-6", "0-7", "1-7", "0-6/2", "1-5/2", "1,3,5", "*/2", "1/2", "0/3",
        "SUN", "MON-FRI"
    ];

    private static readonly string[] daysOfMonth = ["1", "1,15", "*/5", "10-20", "29"];

    // "minute hour", the two fields ahead of the day fields.
    private static readonly string[] times = ["0 *", "30 4", "*/15 *", "5,35 9-17"];

    private static readonly string[] months = ["*", "3", "1,7"];

    public static IEnumerable<TestCaseData> Expressions()
    {
        foreach (string dayOfWeek in daysOfWeek)
        {
            foreach (string time in times)
            {
                foreach (string month in months)
                {
                    yield return new TestCaseData($"{time} * {month} {dayOfWeek}");
                }
            }
        }

        foreach (string dayOfMonth in daysOfMonth)
        {
            foreach (string time in times)
            {
                foreach (string month in months)
                {
                    yield return new TestCaseData($"{time} {dayOfMonth} {month} *");
                }
            }
        }
    }

    [TestCaseSource(nameof(Expressions))]
    public void QuartzReadsACrontabLineTheWayCrontabDoes(string unix)
    {
        CrontabSchedule? reference = CrontabSchedule.TryParse(unix);
        if (reference is null)
        {
            Assert.Ignore($"NCrontab does not read '{unix}', so it cannot be the reference for it.");
            return;
        }

        DateTime start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime end = start.AddDays(60);

        List<DateTime> expected = reference.GetNextOccurrences(start, end).ToList();
        List<DateTime> actual = FireTimes(CronExpression.Parse(unix, CronFormat.Unix).WithTimeZone(TimeZoneInfo.Utc), start, end);

        actual.Should().Equal(expected,
            $"'{unix}' read as CronFormat.Unix must name the instants crontab names, and Quartz reads it as "
            + $"'{CronExpression.Parse(unix, CronFormat.Unix).CronExpressionString}'");
    }

    [Test]
    public void TheOracleActuallyRanOnMostOfTheGrid()
    {
        int readable = Expressions()
            .Select(x => (string) x.Arguments[0]!)
            .Count(x => CrontabSchedule.TryParse(x) is not null);

        readable.Should().BeGreaterThan(Expressions().Count() * 3 / 4,
            "a differential test whose reference rejects most of the grid is not testing anything, "
            + "so the skip count is worth watching rather than tolerating");
    }

    private static List<DateTime> FireTimes(CronExpression expression, DateTime start, DateTime end)
    {
        List<DateTime> times = new List<DateTime>();
        DateTimeOffset cursor = new DateTimeOffset(start, TimeSpan.Zero);
        DateTimeOffset until = new DateTimeOffset(end, TimeSpan.Zero);
        while (true)
        {
            DateTimeOffset? next = expression.GetNextValidTimeAfter(cursor);
            if (next is null || next.Value >= until)
            {
                return times;
            }

            times.Add(next.Value.UtcDateTime);
            cursor = next.Value;
        }
    }
}
