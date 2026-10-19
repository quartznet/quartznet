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

namespace Quartz.Util;

/// <summary>
/// The <c>MisfireThreshold</c> of a <see cref="CronTriggerAttribute" /> or
/// <see cref="SimpleTriggerAttribute" />, read the way the generator reads it.
/// </summary>
/// <remarks>
/// <c>MisfireThresholdLiteralAnalyzer</c> in <c>Quartz.Analyzers</c> gives the same three messages for
/// the same strings, so the compiler and a reflection over the attribute say the same thing.
/// </remarks>
internal static class MisfireThresholdLiteral
{
    /// <summary>
    /// Refuses a stated threshold that is not a <see cref="TimeSpan" /> longer than zero in whole
    /// milliseconds. <see langword="null" /> states none.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not such a <see cref="TimeSpan" />.</exception>
    internal static void Require(string? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            Throw.ArgumentException(
                $"'{value}' is not a TimeSpan. Spell the trigger's misfire threshold the way TimeSpan does, invariantly: \"00:05:00\" for five minutes.",
                parameterName);
        }

        if (parsed <= TimeSpan.Zero)
        {
            Throw.ArgumentException($"A trigger's misfire threshold has to be longer than zero, and '{value}' is not.", parameterName);
        }

        if (parsed.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            Throw.ArgumentException($"A trigger's misfire threshold is a whole number of milliseconds, and '{value}' is not.", parameterName);
        }
    }
}
