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

namespace Quartz;

/// <summary>
/// Which cluster node a trigger prefers to run on.
/// </summary>
/// <remarks>
/// <para>
/// A trigger is either unpinned (<see cref="None" />, the default), pinned to a node the caller
/// names (<see cref="For" />), or pinned to whichever node fires it first (<see cref="Auto" />).
/// An automatic pin stays automatic once claimed: it is released back to the pool when its node
/// stops checking in, whereas a named pin is kept and simply fails over while that node is down.
/// </para>
/// <para>
/// The node name is a scheduler instance id (matching <c>quartz.scheduler.instanceId</c>) and has
/// to match it exactly — pin comparisons happen in SQL using the database's string collation, so a
/// value differing only in case is a different (and on case-sensitive databases, never-matching)
/// node.
/// </para>
/// </remarks>
/// <seealso cref="ITrigger.PreferredNode" />
public readonly record struct PreferredNode
{
    /// <summary>
    /// Stored in the trigger row's preferred-node column to request an automatic pin no node has
    /// claimed yet. Never a legal node name, which is why <see cref="For" /> rejects it.
    /// </summary>
    internal const string AutoSentinel = "*";

    // The pair the triggers table holds: the node column (null, the sentinel, or a node name) and
    // the auto-claim flag. Keeping storage's own shape is what lets the value round-trip through a
    // database row - and through a trigger's binary-serialized fields - without a lossy mapping.
    private readonly string? node;
    private readonly bool automatic;

    private PreferredNode(string? node, bool automatic)
    {
        this.node = node;
        this.automatic = automatic;
    }

    /// <summary>
    /// No preference: any node in the cluster may fire the trigger. The default.
    /// </summary>
    public static PreferredNode None => default;

    /// <summary>
    /// Pin the trigger to whichever node fires it first, and keep it there for as long as that
    /// node is alive.
    /// </summary>
    public static PreferredNode Auto => new(AutoSentinel, automatic: false);

    /// <summary>
    /// Pin the trigger to the node with the given scheduler instance id.
    /// </summary>
    /// <param name="node">
    /// The scheduler instance id of the target node, matching <c>quartz.scheduler.instanceId</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="node" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="node" /> is blank or is a name reserved by the pinning protocol. Use
    /// <see cref="None" /> to clear a pin and <see cref="Auto" /> to request one.
    /// </exception>
    public static PreferredNode For(string node)
    {
        if (node is null)
        {
            Throw.ArgumentNullException(nameof(node));
        }

        string trimmed = node.Trim();

        if (trimmed.Length == 0)
        {
            Throw.ArgumentException($"A preferred node needs a scheduler instance id; use {nameof(PreferredNode)}.{nameof(None)} to clear the pin.", nameof(node));
        }

        if (trimmed == AutoSentinel)
        {
            Throw.ArgumentException($"'{AutoSentinel}' is reserved; use {nameof(PreferredNode)}.{nameof(Auto)} to pin the trigger to the node that first fires it.", nameof(node));
        }

        if (trimmed == "_" || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            Throw.ArgumentException($"Node name '{trimmed}' is reserved.", nameof(node));
        }

        return new PreferredNode(trimmed, automatic: false);
    }

    /// <summary>
    /// The scheduler instance id the trigger is pinned to, or <see langword="null" /> when it has
    /// no pin or is still waiting for a node to claim an <see cref="Auto" /> pin.
    /// </summary>
    public string? Node => node == AutoSentinel ? null : node;

    /// <summary>
    /// Whether the pin is (or, once claimed, was) handed out automatically rather than named by
    /// the caller. An automatic pin is released when its node stops checking in.
    /// </summary>
    public bool IsAutomatic => automatic || node == AutoSentinel;

    /// <summary>
    /// Whether the trigger has no node preference at all.
    /// </summary>
    public bool IsNone => node is null;

    /// <summary>
    /// The value as the triggers table holds it: the preferred-node column, which is
    /// <see langword="null" />, the auto-pin sentinel, or a node name.
    /// </summary>
    internal string? StoredNode => node;

    /// <summary>
    /// The value as the triggers table holds it: the auto-claim flag, which is only ever set
    /// alongside a node name.
    /// </summary>
    internal bool StoredAutomatic => automatic;

    /// <summary>
    /// Rebuilds the value from the pair stored in the triggers table.
    /// </summary>
    internal static PreferredNode FromStored(string? node, bool automatic)
    {
        if (string.IsNullOrWhiteSpace(node))
        {
            return default;
        }

        string trimmed = node!.Trim();
        return new PreferredNode(trimmed, automatic && trimmed != AutoSentinel);
    }

    /// <summary>
    /// The value this one becomes when <paramref name="instanceId" /> claims an <see cref="Auto" />
    /// pin by firing the trigger.
    /// </summary>
    internal static PreferredNode ClaimedBy(string instanceId) => new(instanceId, automatic: true);

    /// <inheritdoc />
    public override string ToString()
    {
        if (node is null)
        {
            return "none";
        }

        if (node == AutoSentinel)
        {
            return "auto";
        }

        return automatic ? node + " (auto)" : node;
    }
}
