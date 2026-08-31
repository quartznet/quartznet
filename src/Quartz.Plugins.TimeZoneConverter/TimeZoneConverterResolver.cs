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

using TimeZoneConverter;

namespace Quartz.Plugins.TimeZoneConverter;

/// <summary>
/// Teaches <see cref="TimeZones" /> to resolve a time zone id through TimeZoneConverter, so that
/// Windows ids and IANA ids both resolve whichever operating system the process is running on.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of what the package does, and it is why there is no plugin here any more. A
/// resolver is process-wide — <see cref="TimeZones.FindById" /> is reached from places that have no
/// scheduler in scope, such as parsing a <see cref="CronExpression" /> or deserializing a trigger out of
/// a job store blob — so there was never any per-scheduler state for a plugin lifecycle to hold, and
/// registering at configuration time means a trigger built before the host starts resolves its zone too.
/// </para>
/// <para>
/// The registration happens once per process and is never removed. A plugin had to be more careful:
/// every scheduler that named it added a resolver of its own and disposed that one on shutdown, so that
/// stopping one scheduler did not change time zone resolution for the others. One registration nobody
/// disposes is the same guarantee with none of the bookkeeping — resolution cannot be taken away from a
/// scheduler that is still running, and a second <c>UseTimeZoneConverter</c> cannot pile a duplicate
/// resolver onto a list that is consulted on every failed lookup.
/// </para>
/// </remarks>
internal static class TimeZoneConverterResolver
{
    private static int registered;

    /// <summary>
    /// Registers the resolver, unless it is already registered.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> if this call is the one that registered it, and
    /// <see langword="false" /> if it was already there.
    /// </returns>
    internal static bool Register()
    {
        if (Interlocked.CompareExchange(ref registered, 1, 0) != 0)
        {
            return false;
        }

        // TryGetTimeZoneInfo rather than GetTimeZoneInfo: a resolver declines an id by answering null,
        // and an id this one does not know is one the next resolver may.
        TimeZones.AddResolver(
            static id => TZConvert.TryGetTimeZoneInfo(id, out TimeZoneInfo? timeZoneInfo) ? timeZoneInfo : null);

        return true;
    }
}
