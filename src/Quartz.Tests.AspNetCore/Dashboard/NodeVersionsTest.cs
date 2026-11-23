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

using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// What the dashboard says about each node's Quartz version, and when a cluster is mixed (#4411).
/// </summary>
public class NodeVersionsTest
{
    private static readonly DateTimeOffset CheckedIn = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void AStampedRowShowsItsStamp()
    {
        NodeVersions.Label(Node("a", "4.9.0"), reports: true, answeringVersion: null).Should().Be("4.9.0");
    }

    [Test]
    public void AnUnstampedRowIsOlderThan46OnlyWhenTheListingReportsStamps()
    {
        ClusterNodeDto old = Node("b", version: null);

        NodeVersions.Label(old, reports: true, answeringVersion: null).Should().Be("older than 4.6",
            "a 4.6 node stamps its row at every check-in, so a row without one was written by an older node");
        NodeVersions.Label(old, reports: false, answeringVersion: null).Should().Be("not reported",
            "a host older than 4.9 sends no version for any node, and an unstamped row then says nothing");
    }

    [Test]
    public void AListingReportsWhenAnyRowCarriesAStampOrItIsAWindows()
    {
        NodeVersions.Reports([Node("a", "4.9.0"), Node("b", null)], isWindow: false).Should().BeTrue();
        NodeVersions.Reports([Node("a", null), Node("b", null)], isWindow: false).Should().BeFalse(
            "a 4.9 store always stamps the node that answers, so a listing with no stamp at all is an older host's");
        NodeVersions.Reports([Node("a", null)], isWindow: true).Should().BeTrue("a window reads the rows itself");
    }

    [Test]
    public void ANodeWithNoRowShowsTheAnsweringVersionWhenItIsTheAnsweringNode()
    {
        ClusterNodeDto current = new("me", LastCheckInUtc: null, CheckInInterval: null, ClusterNodeState.Alive, IsCurrentNode: true);
        ClusterNodeDto peer = current with { InstanceId = "peer", IsCurrentNode = false };

        NodeVersions.Label(current, reports: false, answeringVersion: "4.9.0.0").Should().Be("4.9.0",
            "a store that keeps no check-in rows has one node, and its version is the scheduler's own, as major.minor.patch");
        NodeVersions.Label(peer, reports: false, answeringVersion: "4.9.0.0").Should().BeNull("nothing says which version a rowless peer runs");
    }

    [Test]
    public void LiveNodesOfOneVersionAreNotMixed()
    {
        NodeVersions.Mixed([Node("a", "4.9.0"), Node("b", "4.9.0")], reports: true).Should().BeNull();
    }

    [Test]
    public void AFailedNodeDoesNotMakeAClusterMixed()
    {
        NodeVersions.Mixed([Node("a", "4.9.0"), Node("b", "4.5.0", ClusterNodeState.Failed)], reports: true).Should().BeNull(
            "only a live node changes what the cluster does");
    }

    [Test]
    public void ANodeOlderThan46CostsLockedRoundsAndUnansweredInterrupts()
    {
        MixedVersions mixed = NodeVersions.Mixed([Node("new", "4.9.0"), Node("old", null), Node("late", "4.5.2", ClusterNodeState.Overdue)], reports: true)!;

        mixed.Should().NotBeNull();
        mixed.Groups.Select(group => group.Version).Should().Equal(["older than 4.6", "4.5.2", "4.9.0"]);
        mixed.Costs.Should().ContainSingle().Which.Should()
            .StartWith("While nodes late (4.5.2), old (older than 4.6) are live")
            .And.Contain("takes TRIGGER_ACCESS")
            .And.Contain("never answered");
    }

    /// <summary>
    /// What a difference among nodes of 4.6 or later costs depends on the database, which the dashboard does
    /// not know, so the banner names the versions and claims no cost.
    /// </summary>
    [Test]
    public void A46NodeBesideNewerOnesClaimsNoCost()
    {
        MixedVersions mixed = NodeVersions.Mixed([Node("a", "4.6.3"), Node("b", "4.9.0")], reports: true)!;

        mixed.Groups.Select(group => group.Version).Should().Equal(["4.6.3", "4.9.0"]);
        mixed.Costs.Should().BeEmpty();
    }

    [Test]
    public void NewerNodesThatDifferCostNothingTheDashboardKnowsOf()
    {
        MixedVersions mixed = NodeVersions.Mixed([Node("a", "4.8.1"), Node("b", "4.9.0")], reports: true)!;

        mixed.Groups.Should().HaveCount(2);
        mixed.Costs.Should().BeEmpty();
    }

    [Test]
    public void AListingThatDoesNotReportIsNeverMixed()
    {
        NodeVersions.Mixed([Node("a", null), Node("b", null)], reports: false).Should().BeNull();
    }

    private static ClusterNodeDto Node(string instanceId, string? version, ClusterNodeState state = ClusterNodeState.Alive)
    {
        return new ClusterNodeDto(instanceId, CheckedIn, TimeSpan.FromSeconds(15), state, IsCurrentNode: false) { QuartzVersion = version };
    }
}
