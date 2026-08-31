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

namespace Quartz;

/// <summary>
/// The field layout and day-of-week numbering a cron expression string is read with.
/// </summary>
/// <remarks>
/// <para>
/// The format is chosen at the moment the string is read and is not carried anywhere afterwards:
/// <see cref="CronExpression.Parse(string, CronFormat)" /> normalises what it reads into the
/// canonical Quartz form, which is what <see cref="CronExpression.CronExpressionString" />, the
/// dashboard and the database all hold.
/// </para>
/// <para>
/// The <c>@</c> macros — <c>@yearly</c>, <c>@annually</c>, <c>@monthly</c>, <c>@weekly</c>,
/// <c>@daily</c>, <c>@midnight</c> and <c>@hourly</c> — need no format: they mean the same thing in
/// every cron there is, so they are read by every entry point, including the XML scheduling data
/// files and the HTTP API.
/// </para>
/// </remarks>
/// <seealso cref="CronExpression.Parse(string, CronFormat)" />
/// <seealso cref="CronExpression.TryParse(string, CronFormat, out CronExpression)" />
/// <seealso cref="CronScheduleBuilder.Create(string, CronFormat)" />
public enum CronFormat
{
    /// <summary>
    /// Quartz's own dialect: six or seven fields - seconds, minutes, hours, day-of-month, month,
    /// day-of-week and optionally year - with day-of-week numbered 1-7 starting at Sunday.
    /// </summary>
    Quartz = 0,

    /// <summary>
    /// The Unix crontab dialect: five fields - minutes, hours, day-of-month, month, day-of-week -
    /// with no seconds and no year, and day-of-week numbered 0-7 where both 0 and 7 are Sunday.
    /// </summary>
    Unix = 1,
}
