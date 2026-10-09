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

#nullable enable

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The target registry's one rule: one target per name, whatever kind of target it is, and a name that
/// could be read back out of a key.
/// </summary>
public sealed class SchedulerTargetsTest
{
    [Test]
    public void ANameIsHeldByOneTargetOfAnyKind()
    {
        SchedulerTargets targets = new();
        targets.Add(Target("prod", SchedulerOrigin.Window));

        Action http = () => targets.Add(Target("prod", SchedulerOrigin.Remote));

        http.Should().Throw<SchedulerConfigException>()
            .WithMessage("*'prod'*store attached*HTTP target*",
                "a store and an HTTP target under one name would give two schedulers one spelling, so the "
                + "refusal names both kinds");
    }

    [Test]
    public void NamesAreComparedIgnoringCase()
    {
        SchedulerTargets targets = new();
        targets.Add(Target("prod", SchedulerOrigin.Remote));

        Action again = () => targets.Add(Target("PROD", SchedulerOrigin.Remote));

        again.Should().Throw<SchedulerConfigException>("the repository indexes names ignoring case, and a key is read the same way");
        targets.Find("Prod").Should().NotBeNull();
    }

    [TestCase("a/b", "/")]
    [TestCase("a+b", "+")]
    public void TheSeparatorsAreRefusedInAName(string name, string separator)
    {
        SchedulerTargets targets = new();

        Action add = () => targets.Add(Target(name, SchedulerOrigin.Remote));

        add.Should().Throw<SchedulerConfigException>()
            .WithMessage($"*'{separator}'*", "'/' separates a target from a name in a key, and '+' joins a cluster's members");
    }

    [Test]
    public void ABlankNameIsRefused()
    {
        SchedulerTargets targets = new();

        Action add = () => targets.Add(Target(" ", SchedulerOrigin.Remote));

        add.Should().Throw<SchedulerConfigException>();
    }

    [Test]
    public void RemovingATargetFreesItsNameAndTheChangeIsAnnounced()
    {
        SchedulerTargets targets = new();
        int changes = 0;
        targets.Changed += (_, _) => changes++;

        targets.Add(Target("w1", SchedulerOrigin.Remote));
        targets.Remove("W1").Should().BeTrue();
        targets.Remove("w1").Should().BeFalse("it is gone");

        targets.Find("w1").Should().BeNull();
        changes.Should().Be(2, "the fleet monitor re-evaluates on every addition and removal");

        Action again = () => targets.Add(Target("w1", SchedulerOrigin.Agent));
        again.Should().NotThrow("a removed name is free for the next holder");
    }

    [Test]
    public void TheSnapshotIsInNameOrder()
    {
        SchedulerTargets targets = new();
        targets.Add(Target("zeta", SchedulerOrigin.Remote));
        targets.Add(Target("Alpha", SchedulerOrigin.Window));

        targets.Snapshot().Select(x => x.Name).Should().Equal(["Alpha", "zeta"]);
    }

    [Test]
    public void AClustersNameIsItsMembersSortedAndJoined()
    {
        SchedulerTargets.ClusterTargetName(["c", "a", "B"]).Should().Be("a+B+c",
            "the members are sorted ignoring case so the same membership always spells the same target");
    }

    private static SchedulerTarget Target(string name, SchedulerOrigin origin)
    {
        return new SchedulerTarget { Name = name, Origin = origin };
    }
}
