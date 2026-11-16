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

using System.Runtime.InteropServices;

#nullable enable

namespace Quartz.Tests.Unit;

/// <summary>
/// Defects of a .NET runtime that a test meets through no fault of Quartz's, each ignored only on the
/// exact runtime that has it, so that the next one runs the case again and says whether the fix shipped.
/// </summary>
internal static class RuntimeIssues
{
    /// <summary>
    /// Whether this runtime has TZ-YEAR-1: exactly .NET 11.0.0, on a platform that reads the zone from
    /// TZif files. Windows reads it from the registry, and its 11.0.0 runs the Lord_Howe cases clean.
    /// </summary>
    /// <remarks>
    /// <see cref="RuntimeInformation.FrameworkDescription" /> is <c>.NET 11.0.0</c> on the release and
    /// nothing else: a prerelease's carries its label, <c>.NET 11.0.0-rc.3.26522.148</c>, and a patch its
    /// own number. CoreLib's informational version would not do, as the release carries a label there
    /// too, <c>11.0.0-rtm.26529.118+…</c>.
    /// </remarks>
    internal static bool HasTzYear1 { get; } =
        string.Equals(RuntimeInformation.FrameworkDescription, ".NET 11.0.0", StringComparison.Ordinal)
        && !OperatingSystem.IsWindows();

    /// <summary>
    /// Ignores the Australia/Lord_Howe case where the runtime has TZ-YEAR-1. Where the zone's listed
    /// transitions end and its POSIX rule takes over, the per-year transition cache of
    /// dotnet/runtime#119662 moves its 2038 transitions: at 2037-12-31T13:00Z
    /// <see cref="TimeZoneInfo.GetUtcOffset(DateTimeOffset)" /> answers +10:30, standard time, where the
    /// zone is on daylight time, +11:00, which is the offset Quartz's own reading of the rule gives.
    /// </summary>
    internal static void IgnoreLordHoweWhereTheRuntimeHasTzYear1(string zoneId)
    {
        if (HasTzYear1 && string.Equals(zoneId, "Australia/Lord_Howe", StringComparison.Ordinal))
        {
            Assert.Ignore("On .NET 11.0.0 outside Windows, TimeZoneInfo gives Australia/Lord_Howe an offset of +10:30 at "
                + "2037-12-31T13:00Z, where +11:00 is expected: TZ-YEAR-1, https://github.com/dotnet/runtime/issues/134839.");
        }
    }
}
