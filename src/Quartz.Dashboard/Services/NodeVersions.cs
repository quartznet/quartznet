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

namespace Quartz.Dashboard.Services;

/// <summary>
/// What the Cluster page and the Overview say about the Quartz version each node runs, and what a cluster
/// whose live nodes differ costs (#4411).
/// </summary>
/// <remarks>
/// A node's version comes off its check-in row, which a node stamps from 4.6. A row with no stamp is therefore
/// a node older than 4.6, but only when the source reports stamps at all: a host older than 4.9 sends none,
/// and every one of its rows reads as unstamped. A source reports when it is a window, which reads the rows
/// itself, or when any row carries a stamp: a 4.9 store always stamps the node that answers.
/// </remarks>
internal static class NodeVersions
{
    /// <summary>What a row with no stamp says, in a listing that reports stamps.</summary>
    internal const string OlderThan46 = "older than 4.6";

    /// <summary>What a row with no stamp says, in a listing from a host older than 4.9.</summary>
    internal const string NotReported = "not reported";

    private static readonly Version LockFreeRoundsSince = new(4, 6);

    /// <summary>
    /// Whether <paramref name="nodes" /> say which version their nodes run, so that a row without a stamp is a
    /// node older than 4.6.
    /// </summary>
    /// <param name="nodes">The listing.</param>
    /// <param name="isWindow">Whether the listing is a window's, which reads the rows itself.</param>
    public static bool Reports(IReadOnlyCollection<ClusterNodeDto> nodes, bool isWindow)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return isWindow || nodes.Any(static node => node.QuartzVersion is { Length: > 0 });
    }

    /// <summary>
    /// The version to show for <paramref name="node" />: its stamp; for a node with no check-in row, the
    /// answering node's own version when it is that node; otherwise <see cref="OlderThan46" /> or
    /// <see cref="NotReported" />. <see langword="null" /> for a peer with no row, which says nothing.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="reports">What <see cref="Reports" /> answered for the listing.</param>
    /// <param name="answeringVersion">The answering node's <see cref="SchedulerDetailDto.Version" />, when read.</param>
    public static string? Label(ClusterNodeDto node, bool reports, string? answeringVersion)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.QuartzVersion is { Length: > 0 } version)
        {
            return version;
        }

        if (node.LastCheckInUtc is null)
        {
            return node.IsCurrentNode && answeringVersion is { Length: > 0 } ? Normalize(answeringVersion) : null;
        }

        return reports ? OlderThan46 : NotReported;
    }

    /// <summary>
    /// What a cluster whose live nodes run different versions costs, or <see langword="null" /> when they run
    /// one, or the listing does not say. A failed node is not live, and a node with neither a row nor a stamp
    /// is no cluster's.
    /// </summary>
    /// <remarks>
    /// Only what holds on every supported database is said: while a node older than 4.6 is live, rounds take
    /// <c>TRIGGER_ACCESS</c> and its firings' interrupt requests go unanswered. What a newer difference costs
    /// depends on the database, which the dashboard does not know, so it says nothing about it.
    /// </remarks>
    /// <param name="nodes">The listing.</param>
    /// <param name="reports">What <see cref="Reports" /> answered for it.</param>
    public static MixedVersions? Mixed(IReadOnlyCollection<ClusterNodeDto> nodes, bool reports)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (!reports)
        {
            return null;
        }

        SortedDictionary<string, List<string>> byVersion = new(Comparer<string>.Create(CompareVersions));
        List<(string InstanceId, string Version)> olderThan46 = [];

        foreach (ClusterNodeDto node in nodes)
        {
            if (node.State == ClusterNodeState.Failed || (node.QuartzVersion is null && node.LastCheckInUtc is null))
            {
                continue;
            }

            string version = node.QuartzVersion is { Length: > 0 } stamp ? stamp : OlderThan46;
            if (!byVersion.TryGetValue(version, out List<string>? members))
            {
                members = [];
                byVersion[version] = members;
            }

            members.Add(node.InstanceId);

            if (Parse(version) is not { } parsed || parsed < LockFreeRoundsSince)
            {
                olderThan46.Add((node.InstanceId, version));
            }
        }

        if (byVersion.Count < 2)
        {
            return null;
        }

        List<string> costs = [];
        if (olderThan46.Count > 0)
        {
            costs.Add($"While {Nodes(olderThan46)} {(olderThan46.Count == 1 ? "is" : "are")} live, every round of work that allows "
                + "concurrent execution takes TRIGGER_ACCESS, and an interrupt request for a firing on "
                + (olderThan46.Count == 1 ? "it" : "them") + " is recorded but never answered.");
        }

        List<NodeVersionGroup> groups = new(byVersion.Count);
        foreach (KeyValuePair<string, List<string>> entry in byVersion)
        {
            entry.Value.Sort(StringComparer.Ordinal);
            groups.Add(new NodeVersionGroup(entry.Key, entry.Value));
        }

        return new MixedVersions(groups, costs);
    }

    /// <summary>The older nodes, each with the version it runs: <c>node b (4.5.2)</c>.</summary>
    private static string Nodes(List<(string InstanceId, string Version)> nodes)
    {
        nodes.Sort(static (left, right) => string.CompareOrdinal(left.InstanceId, right.InstanceId));
        return (nodes.Count == 1 ? "node " : "nodes ") + string.Join(", ", nodes.Select(static node => node.InstanceId + " (" + node.Version + ")"));
    }

    /// <summary>
    /// <c>major.minor.patch</c> of a version a scheduler reports, which carries a fourth part and may carry a
    /// prerelease tag; the text as it stands when it does not parse.
    /// </summary>
    private static string Normalize(string version)
    {
        return Parse(version) is { } parsed
            ? string.Create(CultureInfo.InvariantCulture, $"{parsed.Major}.{parsed.Minor}.{Math.Max(parsed.Build, 0)}")
            : version;
    }

    private static Version? Parse(string version)
    {
        int end = version.IndexOfAny(['-', '+', ' ']);
        string numeric = end < 0 ? version : version[..end];
        return Version.TryParse(numeric, out Version? parsed) ? parsed : null;
    }

    /// <summary>Oldest first, so the banner reads from the node holding the cluster back; unparsed text last.</summary>
    private static int CompareVersions(string left, string right)
    {
        Version? l = string.Equals(left, OlderThan46, StringComparison.Ordinal) ? new Version(0, 0) : Parse(left);
        Version? r = string.Equals(right, OlderThan46, StringComparison.Ordinal) ? new Version(0, 0) : Parse(right);
        if (l is not null && r is not null && l != r)
        {
            return l.CompareTo(r);
        }

        if (l is null != r is null)
        {
            return l is null ? 1 : -1;
        }

        return string.CompareOrdinal(left, right);
    }
}

/// <summary>
/// The live nodes of a cluster, by the version they run, and what the difference costs.
/// </summary>
/// <param name="Groups">Each version and the nodes running it, oldest first.</param>
/// <param name="Costs">What the older nodes cost the cluster while they are live, on every database; empty when
/// every live node is 4.6 or later.</param>
internal sealed record MixedVersions(IReadOnlyList<NodeVersionGroup> Groups, IReadOnlyList<string> Costs);

/// <summary>One version, and the live nodes that run it.</summary>
internal sealed record NodeVersionGroup(string Version, IReadOnlyList<string> InstanceIds);
