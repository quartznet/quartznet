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

using System.Reflection;

#nullable enable

namespace Quartz.Tests.Unit;

/// <summary>
/// Defects of a prerelease .NET runtime that a test meets through no fault of Quartz's, each ignored
/// only on the prerelease that has it, so that the released runtime runs the case again and says
/// whether the fix shipped.
/// </summary>
internal static class PrereleaseRuntimeIssues
{
    /// <summary>
    /// Whether this is a .NET 11 prerelease: its CoreLib's informational version carries a prerelease
    /// label, <c>11.0.0-rc.3.26522.148+…</c>, where a released one is <c>11.0.0+…</c>.
    /// </summary>
    internal static bool IsPrereleaseNet11 { get; } =
        Environment.Version.Major == 11
        && typeof(object).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Contains('-') == true;

    /// <summary>
    /// Ignores the Australia/Lord_Howe case on a .NET 11 prerelease, which disagrees with itself where the
    /// zone's listed transitions end and its POSIX rule takes over: at 2037-12-31T13:00Z
    /// <see cref="TimeZoneInfo.GetUtcOffset(DateTimeOffset)" /> and the local-to-UTC conversion give
    /// different offsets, because the per-year transition cache of dotnet/runtime#119662 moves the zone's
    /// 2038 transitions (TZ-YEAR-1 in dotnet/runtime#134839).
    /// </summary>
    internal static void IgnoreLordHoweOnPrereleaseNet11(string zoneId)
    {
        if (IsPrereleaseNet11 && string.Equals(zoneId, "Australia/Lord_Howe", StringComparison.Ordinal))
        {
            Assert.Ignore("On a .NET 11 prerelease, Australia/Lord_Howe's offset at 2037-12-31T13:00Z disagrees between GetUtcOffset and "
                + "the local-to-UTC conversion: TZ-YEAR-1 in dotnet/runtime#134839.");
        }
    }
}
