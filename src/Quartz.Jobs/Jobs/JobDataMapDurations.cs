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

namespace Quartz.Jobs;

/// <summary>
/// Reads the durations the scanning jobs keep in job data as a millisecond count.
/// </summary>
internal static class JobDataMapDurations
{
    /// <summary>
    /// Reads a duration stored as milliseconds, or <see langword="null" /> when the key is absent or
    /// holds nothing that can be read as one.
    /// </summary>
    /// <remarks>
    /// Milliseconds is what every version of these jobs has written, so that is what is read — as a
    /// number, or as the string a store in <c>StoreJobDataAsStrings</c> mode leaves behind. A
    /// <see cref="TimeSpan" /> someone put there by hand is taken at face value.
    /// </remarks>
    internal static TimeSpan? ReadMilliseconds(JobDataMap data, string key)
    {
        if (!data.TryGetValue(key, out object? value))
        {
            return null;
        }

        if (value is TimeSpan span)
        {
            return span;
        }

        return data.TryGetLong(key, out long milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds)
            : null;
    }
}
