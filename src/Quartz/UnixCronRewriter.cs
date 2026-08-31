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

namespace Quartz;

/// <summary>
/// Rewrites a five-field Unix crontab expression into the canonical six-field Quartz form.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CronFormat.Unix" /> is a way of reading a string, not a second parser. Everything past
/// this class - the parser, the evaluator, the serializers, the dashboard and the database - only
/// ever sees Quartz's own form, which is what lets a crontab line be scheduled, stored, displayed
/// and read back with no format recorded anywhere.
/// </para>
/// <para>
/// Two things differ between the dialects. The layout: crontab has no seconds and no year, so a
/// seconds field of <c>0</c> is prepended. And the day-of-week numbering: crontab counts 0-6 from
/// Sunday and accepts 7 for Sunday as well, where Quartz counts 1-7 from Sunday, so every bare
/// integer in that field maps <c>n</c> to <c>(n % 7) + 1</c>. Everything else - names, ranges,
/// steps, lists, and Quartz's own <c>L</c>, <c>W</c>, <c>#</c> and <c>H</c> - is spelled the same
/// way in both and travels through untouched.
/// </para>
/// <para>
/// The rewrite runs before the parser, and therefore before <c>H</c> tokens are resolved: an
/// <c>H</c> in a five-field day-of-week is hashed over Quartz's 1-7 range, which is the right range
/// by the time anything looks at it.
/// </para>
/// </remarks>
internal static class UnixCronRewriter
{
    private static readonly char[] fieldSeparators = { ' ', '\t' };

    /// <summary>
    /// Rewrites a Unix crontab expression into the equivalent Quartz expression.
    /// </summary>
    /// <param name="cronExpression">A five-field crontab expression, or an <c>@</c> macro.</param>
    /// <exception cref="FormatException">The expression does not have five fields, or its day-of-week
    /// field names a day the crontab form does not have.</exception>
    internal static string ToQuartz(string cronExpression)
    {
        string expression = CultureInfo.InvariantCulture.TextInfo.ToUpper(cronExpression).Trim();

        // A macro carries no dialect, so it is left for the constructor to expand once for both formats.
        if (expression.Length > 0 && expression[0] == '@')
        {
            return expression;
        }

        string[] fields = expression.Split(fieldSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
        {
            Throw.FormatException(
                $"Cron expression '{expression}' has {fields.Length} fields, but the Unix/crontab form read by "
                + "CronFormat.Unix has exactly 5: minutes, hours, day-of-month, month and day-of-week. "
                + (fields.Length is 6 or 7
                    ? "Six or seven fields is Quartz's own form, which CronFormat.Quartz reads."
                    : "Quartz's own form has six or seven fields, seconds first, and CronFormat.Quartz reads that."));
        }

        string dayOfMonth = fields[2];
        string dayOfWeek = RewriteDayOfWeek(fields[4]);

        // Quartz writes "this field names no days" as '?' in one of the two day fields and '*' in the
        // other. The two say the same thing, so this decides only how the canonical string reads.
        if (dayOfWeek is "*" or "?")
        {
            dayOfWeek = "?";
        }
        else if (dayOfMonth is "*" or "?")
        {
            dayOfMonth = "?";
        }

        return $"0 {fields[0]} {fields[1]} {dayOfMonth} {fields[3]} {dayOfWeek}";
    }

    private static string RewriteDayOfWeek(string field)
    {
        if (!field.Contains(','))
        {
            return RewriteToken(field);
        }

        string[] tokens = field.Split(',');
        for (int i = 0; i < tokens.Length; i++)
        {
            tokens[i] = RewriteToken(tokens[i]);
        }

        return string.Join(",", tokens);
    }

    private static string RewriteToken(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        // '*' and '?' name no day, 'L' on its own is Saturday in both readings, and an 'H' token is a
        // hash over the whole field - none of them carries a Unix day number, and a step on any of them
        // walks the same days whichever dialect wrote it.
        if (token[0] is '*' or '?' or 'L' or 'H')
        {
            return token;
        }

        int slash = token.IndexOf('/');
        bool stepped = slash >= 0;
        string body = stepped ? token.Substring(0, slash) : token;
        string step = stepped ? token.Substring(slash) : "";

        int dash = body.IndexOf('-');
        if (dash <= 0)
        {
            return RewriteValue(body, stepped) + step;
        }

        string start = body.Substring(0, dash);
        string end = body.Substring(dash + 1);

        if (TryReadUnixDay(start, out int rawStart) && TryReadUnixDay(end, out int rawEnd) && rawEnd - rawStart >= 6)
        {
            // The range spans the whole week, which renumbering alone would collapse: '0-7' becomes
            // 'SUN-SUN'. Say "every day" instead - or, when a step picks days out of the range, a range
            // that wraps around to the day before it started, so the step keeps the phase Unix gave it.
            if (!stepped)
            {
                return "*";
            }

            int first = ToQuartzDay(rawStart);
            int last = first == 1 ? 7 : first - 1;
            return $"{first}-{last}{step}";
        }

        return RewriteValue(start, stepped) + "-" + RewriteValue(end, stepped) + step;
    }

    /// <summary>
    /// Renumbers one day-of-week value, keeping whatever <c>L</c> or <c>#n</c> suffix follows it.
    /// </summary>
    private static string RewriteValue(string value, bool stepped)
    {
        int digits = 0;
        while (digits < value.Length && char.IsAsciiDigit(value[digits]))
        {
            digits++;
        }

        if (digits == 0)
        {
            // A name, or something the parser will have its own opinion about; either way it reads the
            // same in both dialects.
            return value;
        }

        if (!int.TryParse(value.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out int raw) || raw > 7)
        {
            Throw.FormatException(
                $"'{value}' is not a day of the week: the Unix/crontab form numbers them 0-7, with both 0 and 7 meaning Sunday.");
        }

        int day = ToQuartzDay(raw);

        // The name is the clearer thing to store - nobody reads 'MON' as the Unix 1 it came from - but
        // only where it can stand on its own. A name in front of a step is 'MON/2', which Quartz rejects
        // outright, and a name in front of a suffix would make 'SUNL' and '1L' two spellings of one day.
        return digits == value.Length && !stepped
            ? CronExpression.DayOfWeekNames[day]
            : string.Concat(day.ToString(CultureInfo.InvariantCulture), value.AsSpan(digits));
    }

    private static bool TryReadUnixDay(string value, out int day)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out day) && day <= 7;
    }

    private static int ToQuartzDay(int unixDay)
    {
        return unixDay % 7 + 1;
    }
}
