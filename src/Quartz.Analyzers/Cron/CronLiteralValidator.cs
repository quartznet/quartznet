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

namespace Quartz.Analyzers;

/// <summary>
/// Reads a cron literal with the linked parser and answers with what the call site would have thrown.
/// </summary>
/// <remarks>
/// The whole of the analyzer's cron knowledge is these few lines, because every rule it enforces is
/// the parser's. <c>Quartz.Analyzers.Tests.CronParityTest</c> runs a corpus through this and through
/// <c>Quartz.CronExpression</c> and fails when the two disagree about validity or about the message.
/// The one rule about a valid expression, QZ0007, is a member of the parsed expression too, which the
/// scheduler asks at run time.
/// </remarks>
internal static class CronLiteralValidator
{
    /// <summary>
    /// The message the entry point would have thrown for this literal, or <see langword="null" />
    /// when it parses.
    /// </summary>
    /// <param name="expression">The literal, as written.</param>
    /// <param name="format">The dialect the entry point reads it in.</param>
    /// <param name="resolvesHash">
    /// Whether the entry point resolves <c>H</c> tokens. The ones that do accept an expression the
    /// ones that do not reject, so this is not a detail: <c>WithCronSchedule("0 H 3 * * ?")</c> is
    /// valid and <c>CronExpression.Parse("0 H 3 * * ?")</c> is not.
    /// </param>
    /// <param name="parsed">
    /// The expression the parser built, when it parses, so that a rule about a valid expression asks
    /// the parser's own fields rather than reading the string a second time.
    /// </param>
    internal static string? Validate(string expression, CronFormat format, bool resolvesHash, out CronExpression? parsed)
    {
        parsed = null;

        try
        {
            // The seed is irrelevant to whether the expression parses - which values H resolves to
            // does not decide that - so CronScheduleBuilder.Create uses 0 here for the same reason.
            parsed = resolvesHash
                ? CronExpression.ParseWithHash(expression, format, hashSeed: 0)
                : CronExpression.Parse(expression, format);

            return null;
        }
        catch (FormatException e)
        {
            return e.Message;
        }
        catch (ArgumentException e)
        {
            // A field whose range runs backwards where it may not, and the out-of-range guards the
            // parser raises rather than formats - ArgumentOutOfRangeException derives from this.
            return e.Message;
        }
    }
}
