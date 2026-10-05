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

using System.Collections.Concurrent;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Quartz.Impl;

/// <summary>
/// Where one client reports what a newer host sent it that it has no name for: one line per name, however
/// often the name arrives.
/// </summary>
/// <remarks>
/// <para>
/// One per reader of one remote scheduler, so the line names the scheduler, and a second client of another
/// target logs its own. A dashboard refreshes a page every few seconds and each refresh meets the same
/// names, so a line per meeting would be the same line for as long as the client stays old.
/// </para>
/// <para>
/// The set of names already reported is bounded: past <see cref="MaxRemembered" /> a new name is not
/// logged at all, so a host that answers a stream of distinct names costs this process nothing that grows.
/// </para>
/// </remarks>
internal sealed class UnknownWireNames
{
    /// <summary>
    /// How many distinct names one client reports before it stops reporting new ones.
    /// </summary>
    internal const int MaxRemembered = 256;

    private readonly ConcurrentDictionary<string, bool> reported = new(StringComparer.Ordinal);
    private readonly ILogger logger;
    private readonly string schedulerName;

    /// <param name="logger">Where the lines go.</param>
    /// <param name="schedulerName">The remote scheduler, which every line names.</param>
    public UnknownWireNames(ILogger logger, string schedulerName)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.schedulerName = schedulerName;
    }

    /// <summary>
    /// A live event of a kind this client does not know was skipped.
    /// </summary>
    public void EventKindSkipped(string kind)
    {
        if (FirstTime($"kind\u0000{kind}"))
        {
            logger.UnknownEventKindSkipped(kind, schedulerName);
        }
    }

    /// <summary>
    /// A live event whose body could not be read was skipped.
    /// </summary>
    /// <remarks>
    /// Once per SSE event type rather than per frame, for the reason every line here is once: a host that
    /// writes one malformed kind writes it on every occurrence.
    /// </remarks>
    public void EventUnreadable(string kind, JsonException exception)
    {
        if (FirstTime($"unreadable\u0000{kind}"))
        {
            logger.UnreadableEventSkipped(kind, schedulerName, exception);
        }
    }

    /// <summary>
    /// An item of a listing was left out, because a member the item cannot be built without carries a name
    /// this client does not know.
    /// </summary>
    /// <param name="item">What the listing lists, as an operator would say it.</param>
    /// <param name="unknown">The name, and the enum it was read as.</param>
    public void ItemLeftOut(string item, UnknownWireNameException unknown)
    {
        if (FirstTime($"item\u0000{item}\u0000{unknown.EnumType.Name}\u0000{unknown.Name}"))
        {
            logger.UnknownItemLeftOut(item, schedulerName, unknown.EnumType.Name, unknown.Name);
        }
    }

    /// <summary>
    /// A name this client does not know was read as something it does.
    /// </summary>
    /// <param name="enumType">The enum the name was read as.</param>
    /// <param name="name">The name the host sent.</param>
    /// <param name="readAs">What it was read as: a member's name, or <c>null</c>.</param>
    public void ReadAs(Type enumType, string name, string readAs)
    {
        if (FirstTime($"read\u0000{enumType.Name}\u0000{name}"))
        {
            logger.UnknownNameReadAs(enumType.Name, name, schedulerName, readAs);
        }
    }

    private bool FirstTime(string key)
    {
        // The lookup first: it takes no lock, and a name met again is the common case once one has been met.
        if (reported.ContainsKey(key) || reported.Count >= MaxRemembered)
        {
            return false;
        }

        return reported.TryAdd(key, true);
    }
}
