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
/// The <c>@</c> macros every cron implementation since Vixie's has understood.
/// </summary>
/// <remarks>
/// <para>
/// A macro names a schedule rather than spelling one, and it names the same schedule in every cron
/// there is, so it carries no dialect: <see cref="CronFormat" /> has nothing to say about it and it
/// is expanded by the constructor, which puts it behind every entry point at once - the XML
/// scheduling files, the HTTP API and the dashboard's expression box included.
/// </para>
/// <para>
/// This is Vixie's set and only Vixie's set. There is no <c>@every_minute</c> or <c>@every_second</c>
/// - <c>0 * * * * ?</c> and <c>* * * * * ?</c> are already short - and no jittered variant, because
/// Quartz's <c>H</c> token spreads load deterministically, which is better than a random one.
/// </para>
/// </remarks>
internal static class CronMacros
{
    /// <summary>
    /// Expands a leading <c>@</c> macro into its canonical Quartz expression, or returns
    /// <paramref name="upperExpression" /> unchanged when it is not a macro.
    /// </summary>
    /// <param name="upperExpression">A trimmed, upper-cased cron expression string.</param>
    /// <exception cref="FormatException">The string starts with <c>@</c> but names no known macro.</exception>
    internal static string Expand(string upperExpression)
    {
        if (upperExpression.Length == 0 || upperExpression[0] != '@')
        {
            return upperExpression;
        }

        return upperExpression switch
        {
            "@YEARLY" or "@ANNUALLY" => "0 0 0 1 1 ?",
            "@MONTHLY" => "0 0 0 1 * ?",
            "@WEEKLY" => "0 0 0 ? * SUN",
            "@DAILY" or "@MIDNIGHT" => "0 0 0 * * ?",
            "@HOURLY" => "0 0 * * * ?",
            "@REBOOT" => throw new FormatException(
                "'@reboot' is not supported: a scheduler has no reboot to fire on. Run the work as the "
                + "application starts instead - schedule the job with a trigger that starts now - or give "
                + "the trigger a start time and let it fire on its own schedule from there."),
            _ => throw new FormatException(
                $"Unknown cron macro '{upperExpression}'. The supported macros are @yearly (@annually), "
                + "@monthly, @weekly, @daily (@midnight) and @hourly."),
        };
    }
}
