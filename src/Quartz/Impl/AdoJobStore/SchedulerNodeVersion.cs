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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The Quartz version a node stamps its check-in row with, and what a peer's stamp has to say for this
/// node to fire a round of work that allows concurrent execution without <c>TRIGGER_ACCESS</c> (#4067).
/// </summary>
/// <remarks>
/// <para>
/// A 4.5 node's misfire handler and trigger editors write a row they read by key, under the lock. A
/// 4.6 node's round of concurrent-allowed work takes no lock, so only a 4.6 node's own writes are held
/// to the row as read — and a 4.6 node cannot guard writes a 4.5 node makes. It therefore keeps the
/// lock for every round while any live peer is older than this, and a peer that has never written the
/// column at all is older by definition.
/// </para>
/// <para>
/// The stamp is the assembly's version, <c>major.minor.patch</c>, with no prerelease tag: a
/// <c>4.6.0-beta.1</c> node carries every 4.6 guard. A stamp that does not parse is treated as an older
/// node, since nothing is known about what it does.
/// </para>
/// </remarks>
internal static class SchedulerNodeVersion
{
    /// <summary>The release whose rounds hold their writes to the row as read.</summary>
    internal static readonly Version LockFreeRoundsSince = new(4, 6);

    /// <summary>
    /// This node's version as it is written to <c>QRTZ_SCHEDULER_STATE.QUARTZ_VERSION</c>: the Quartz
    /// assembly's major, minor and build, which the build stamps from the package version.
    /// </summary>
    internal static readonly string Current = Format(typeof(SchedulerNodeVersion).Assembly.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Whether a peer that stamped its row <paramref name="quartzVersion" /> holds its misfire and edit
    /// writes to the row as read, so that a round beside it may take no lock.
    /// </summary>
    /// <param name="quartzVersion">The peer's stamp, or <see langword="null" /> for a row it never stamped.</param>
    internal static bool SupportsLockFreeRounds(string? quartzVersion)
    {
        return IsAtLeast(quartzVersion, LockFreeRoundsSince);
    }

    /// <summary>The release whose nodes read the interrupt requests recorded on their firings (#4071).</summary>
    internal static readonly Version InterruptRequestsSince = new(4, 6);

    /// <summary>
    /// Whether a peer that stamped its row <paramref name="quartzVersion" /> reads the interrupt requests
    /// recorded on its firings, so that a request for one of them is answered.
    /// </summary>
    /// <param name="quartzVersion">The peer's stamp, or <see langword="null" /> for a row it never stamped.</param>
    internal static bool SupportsInterruptRequests(string? quartzVersion)
    {
        return IsAtLeast(quartzVersion, InterruptRequestsSince);
    }

    private static bool IsAtLeast(string? quartzVersion, Version since)
    {
        return quartzVersion is not null
               && Version.TryParse(quartzVersion, out Version? version)
               && version >= since;
    }

    private static string Format(Version version)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}");
    }
}
