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

using System.Collections.Specialized;

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// The one property <c>quartz.scheduler.instanceId = AUTO</c> exists to provide: two nodes of one
/// cluster end up with ids that differ. A cluster whose nodes share an id is not a cluster — each of
/// them reads the others' fired triggers as its own, and the one <c>SCHEDULER_STATE</c> row they all
/// write over says only that somebody checked in.
/// </summary>
/// <remarks>
/// Every generation here happens on one machine, which is exactly the arrangement the reports are
/// about: replicas of one container image all report the same host name, so a generator that leans on
/// the host name alone hands every replica the same id. Nothing in this fixture assumes a particular
/// host name — it asserts that the ids differ, and that what they differ in is not the host part.
/// </remarks>
public class AutoInstanceIdTest
{
    /// <summary>
    /// How many ids the loop asks for. <see cref="SimpleInstanceIdGenerator" /> appends
    /// <see cref="TimeProvider.GetTimestamp" /> — the high-resolution counter, which no
    /// <see cref="TimeProvider" /> a test can inject reaches — so distinctness cannot be driven from a
    /// fake clock and is asserted over a run of back-to-back generations instead. Back-to-back is the
    /// hard case: two nodes starting out of one process, or one image, are as close together as ids
    /// ever get, and a generator that repeats at all repeats here.
    /// </summary>
    private const int Generations = 200;

    [Test]
    public async Task TheGeneratorBehindAutoNeverHandsOutOneIdTwice()
    {
        SimpleInstanceIdGenerator generator = new();

        List<string> ids = [];
        for (int i = 0; i < Generations; i++)
        {
            ids.Add(await generator.GenerateInstanceId());
        }

        ids.Should().OnlyHaveUniqueItems(
            "every one of these was generated on one host, which is what every replica of a container "
            + "image is, and two cluster nodes sharing an id read each other's fired triggers as their own");
    }

    [Test]
    public async Task TheGeneratorBehindAutoDoesNotRelyOnTheHostNameToTellNodesApart()
    {
        SimpleInstanceIdGenerator generator = new();

        string first = await generator.GenerateInstanceId();
        string second = await generator.GenerateInstanceId();

        HostPart(first).Should().Be(HostPart(second),
            "both ran on this machine, so the host name in them is the same host name — the state every "
            + "replica of a container image is in");
        first.Should().NotBe(second,
            "with the host part identical, the whole of the difference has to come from what is appended "
            + "to it; a generator that appended nothing would hand every replica one id");
    }

    /// <summary>
    /// The same question asked of a whole scheduler, because <c>AUTO</c> is a configuration key rather
    /// than a type name and the wiring behind it is what an application actually gets.
    /// </summary>
    /// <remarks>
    /// Both nodes are built here in one process and share a scheduler name, which is what a cluster is.
    /// Each <see cref="QuartzSchedulerBuilder" /> owns its own container and its own repository, so this
    /// really is two nodes rather than one looked up twice.
    /// </remarks>
    [Test]
    public async Task TwoNodesConfiguredAutoGetDistinctInstanceIds()
    {
        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "AutoInstanceIdCluster",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.threadPool.maxConcurrency"] = "1",
        };

        await using StandaloneSchedulerFactory firstNode = ClusteredNodeBuilder.Build(properties);
        await using StandaloneSchedulerFactory secondNode = ClusteredNodeBuilder.Build(properties);

        IScheduler first = await firstNode.GetScheduler();
        IScheduler second = await secondNode.GetScheduler();

        first.SchedulerInstanceId.Should().NotBe(QuartzSchedulerOptions.DefaultInstanceId,
            "AUTO over a clustered store means 'generate one', and NON_CLUSTERED is the placeholder for a "
            + "scheduler that shares its database with nobody");
        first.SchedulerInstanceId.Should().NotBe(second.SchedulerInstanceId,
            "two nodes of one cluster that agree on their id are indistinguishable in every table the "
            + "store writes");
    }

    /// <summary>
    /// The digits <see cref="SimpleInstanceIdGenerator" /> appends, removed. A host name that itself
    /// ends in a digit loses that too, which costs nothing here: both ids lose the same characters, so
    /// what is left is still the same string for both whenever the host name is.
    /// </summary>
    private static string HostPart(string instanceId) => instanceId.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
}
