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

namespace Quartz.Tests.Unit;

/// <summary>
/// An overflowing range — a larger value on the left of the dash than on the right — is a documented
/// <see cref="CronExpression" /> feature, and the three shapes below are the ones its own remarks use
/// as examples: <c>22-2</c> for ten at night through two in the morning, <c>NOV-FEB</c> for a winter,
/// <c>FRI-MON</c> for a long weekend.
/// </summary>
/// <remarks>
/// What was already tested is that such an expression parses to a field that is not empty. That is a
/// weak guard: a wrap silently read as the plain forward range, or as the whole field, passes it and
/// then fires on something nobody asked for. So each case here says which values the field resolves
/// to and when the expression actually fires.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class CronExpressionWrappingRangeTest
{
    [Test]
    public void AnHourRangeWrapsThroughMidnight()
    {
        CronExpression cron = new CronExpression("0 0 22-2 * * ?", TimeZoneInfo.Utc);

        cron.GetSet(CronExpressionConstants.Hour).Should().Equal([0, 1, 2, 22, 23],
            "22-2 is the five hours from ten at night to two in the morning, not the twenty-one hours between 2 and 22");

        FireTimes(cron, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), 7).Should().Equal(
        [
            new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 23, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 2, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 2, 2, 0, 0, TimeSpan.Zero)
        ]);

        cron.IsSatisfiedBy(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero)).Should().BeFalse(
            "midday is on the far side of the wrap");
    }

    [Test]
    public void ADayOfWeekRangeWrapsThroughSunday()
    {
        CronExpression cron = new CronExpression("0 0 12 ? * FRI-MON", TimeZoneInfo.Utc);

        cron.GetSet(CronExpressionConstants.DayOfWeek).Should().Equal([1, 2, 6, 7],
            "the wrap runs FRI, SAT, SUN, MON, and Quartz numbers the week 1 = SUN through 7 = SAT");

        // 2024-01-01 is a Monday, so the long weekend it belongs to has already begun.
        FireTimes(cron, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), 6).Should().Equal(
        [
            new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),  // Monday
            new DateTimeOffset(2024, 1, 5, 12, 0, 0, TimeSpan.Zero),  // Friday
            new DateTimeOffset(2024, 1, 6, 12, 0, 0, TimeSpan.Zero),  // Saturday
            new DateTimeOffset(2024, 1, 7, 12, 0, 0, TimeSpan.Zero),  // Sunday
            new DateTimeOffset(2024, 1, 8, 12, 0, 0, TimeSpan.Zero),  // Monday
            new DateTimeOffset(2024, 1, 12, 12, 0, 0, TimeSpan.Zero)  // Friday
        ]);

        cron.IsSatisfiedBy(new DateTimeOffset(2024, 1, 3, 12, 0, 0, TimeSpan.Zero)).Should().BeFalse(
            "Wednesday is midweek, which is exactly what the wrap excludes");
    }

    [Test]
    public void AMonthRangeWrapsThroughNewYear()
    {
        CronExpression cron = new CronExpression("0 0 12 1 NOV-FEB ?", TimeZoneInfo.Utc);

        cron.GetSet(CronExpressionConstants.Month).Should().Equal([1, 2, 11, 12],
            "the wrap runs November through February, and Quartz numbers the months 1 = January");

        FireTimes(cron, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), 5).Should().Equal(
        [
            new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 2, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 11, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero)
        ]);

        cron.IsSatisfiedBy(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero)).Should().BeFalse(
            "June is summer, on the far side of the wrap");
    }

    /// <summary>
    /// A wrap is written the same way in seconds and minutes, and the two are read by the same code as
    /// the hour field, so one case covers both rather than repeating the hour test twice.
    /// </summary>
    [Test]
    public void SecondAndMinuteRangesWrapThroughTheTopOfTheirField()
    {
        CronExpression cron = new CronExpression("58-1 59-0 12 * * ?", TimeZoneInfo.Utc);

        cron.GetSet(CronExpressionConstants.Second).Should().Equal([0, 1, 58, 59]);
        cron.GetSet(CronExpressionConstants.Minute).Should().Equal([0, 59]);

        FireTimes(cron, new DateTimeOffset(2024, 1, 1, 11, 0, 0, TimeSpan.Zero), 5).Should().Equal(
        [
            new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 12, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 12, 0, 58, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 12, 0, 59, TimeSpan.Zero),
            new DateTimeOffset(2024, 1, 1, 12, 59, 0, TimeSpan.Zero)
        ]);
    }

    private static List<DateTimeOffset> FireTimes(CronExpression cron, DateTimeOffset after, int count)
    {
        List<DateTimeOffset> result = [];
        DateTimeOffset cursor = after;

        for (int i = 0; i < count; i++)
        {
            DateTimeOffset? next = cron.GetNextValidTimeAfter(cursor);
            if (next is null)
            {
                break;
            }

            result.Add(next.Value);
            cursor = next.Value;
        }

        return result;
    }
}
