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

namespace Quartz.Jobs;

/// <summary>
/// Reads the "when did we last see this" timestamps the scanning jobs keep in their own job data.
/// </summary>
internal static class JobDataMapTimestamps
{
    /// <summary>
    /// Reads a timestamp a previous run wrote, or <see langword="null" /> when there is no usable one.
    /// </summary>
    /// <remarks>
    /// Before 4.0 these jobs stored a local <see cref="DateTime" />. A stored <see cref="DateTime" />
    /// is therefore still accepted and read as the instant it denoted, so a job whose state survives
    /// the upgrade does not re-notify its listener for every file it has already reported.
    /// </remarks>
    internal static DateTimeOffset? ReadTimestamp(this JobDataMap map, string key)
    {
        if (!map.TryGetValue(key, out object? value))
        {
            return null;
        }

        return value switch
        {
            DateTimeOffset stored => stored,
            DateTime legacy => new DateTimeOffset(legacy),
            string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) => parsed,
            _ => null,
        };
    }
}
