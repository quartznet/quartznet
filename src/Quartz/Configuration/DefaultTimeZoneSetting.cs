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

using Microsoft.Extensions.Configuration;

namespace Quartz.Configuration;

/// <summary>
/// Reads <see cref="QuartzSchedulerOptions.DefaultTimeZone" /> out of configuration, where it is written as
/// a time zone id.
/// </summary>
/// <remarks>
/// The scheduler section's binding is split. The configuration binding generator cannot construct a
/// <see cref="TimeZoneInfo" />, which has no public constructor, so it skips the property, and
/// <see cref="Bind" /> reads it instead. The flat <c>quartz.scheduler.defaultTimeZone</c> key goes through
/// <see cref="Resolve" /> too, so both spellings accept the same ids and refuse an unknown one alike.
/// </remarks>
internal static class DefaultTimeZoneSetting
{
    /// <summary>
    /// Sets <see cref="QuartzSchedulerOptions.DefaultTimeZone" /> from the scheduler section's
    /// <c>DefaultTimeZone</c> value, when it has one.
    /// </summary>
    internal static void Bind(QuartzSchedulerOptions options, IConfiguration schedulerSection)
    {
        IConfigurationSection section = schedulerSection.GetSection(nameof(QuartzSchedulerOptions.DefaultTimeZone));
        if (!string.IsNullOrWhiteSpace(section.Value))
        {
            options.DefaultTimeZone = Resolve(section.Value, section.Path);
        }
    }

    /// <summary>
    /// The zone <paramref name="id" /> names, as <see cref="TimeZones.FindById" /> resolves it.
    /// </summary>
    /// <param name="id">An IANA or Windows time zone id.</param>
    /// <param name="key">The key the id was read from, which the message names.</param>
    /// <exception cref="SchedulerConfigException">The system cannot resolve <paramref name="id" />.</exception>
    internal static TimeZoneInfo Resolve(string id, string key)
    {
        try
        {
            return TimeZones.FindById(id.Trim());
        }
        catch (TimeZoneNotFoundException e)
        {
            throw new SchedulerConfigException(
                $"Value '{id}' of '{key}' is not a time zone this system can resolve. Name it by an IANA id such as "
                + $"'Europe/Helsinki' or a Windows id such as 'FLE Standard Time'. {e.Message}",
                e);
        }
    }
}
