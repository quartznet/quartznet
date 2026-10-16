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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a node writes into <c>QRTZ_SCHEDULER_STATE.QUARTZ_VERSION</c>, and what a peer's stamp has to say
/// for a firing round beside it to take no lock (#4067).
/// </summary>
public sealed class SchedulerNodeVersionTest
{
    [Test]
    public void TheStampIsTheAssemblysMajorMinorAndPatch()
    {
        Version assembly = typeof(IScheduler).Assembly.GetName().Version!;

        SchedulerNodeVersion.Current.Should().Be($"{assembly.Major}.{assembly.Minor}.{assembly.Build}",
            "the stamp is what the build stamps the assembly with, without a revision or a prerelease tag, so a 4.6.0-beta node reads as 4.6.0");
        SchedulerNodeVersion.SupportsLockFreeRounds(SchedulerNodeVersion.Current).Should().BeTrue(
            "this build carries every guard, so a peer running it is one a lock-free round can run beside");
    }

    [TestCase("4.6.0", true)]
    [TestCase("4.6.3", true)]
    [TestCase("4.7.0", true)]
    [TestCase("5.0.0", true)]
    [TestCase("4.5.1", false)]
    [TestCase("4.5.99", false)]
    [TestCase("3.22.5", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("four point six", false)]
    [TestCase("4.6.0-beta.1", false)]
    public void APeerAllowsLockFreeRoundsOnlyWithAReadableStampOfFourSixOrLater(string? stamp, bool allows)
    {
        SchedulerNodeVersion.SupportsLockFreeRounds(stamp).Should().Be(allows,
            "a row a 4.5 node wrote says nothing, and a stamp nothing can read says nothing either: both are nodes whose writes the lock has to keep off a fired row");
    }
}
