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

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Which store a key's history is read from, on the one case where reading the key and resolving it
/// could disagree.
/// </summary>
public sealed class ExecutionHistoryLookupTest
{
    /// <summary>
    /// A scheduler of this process named <c>x/y</c> beside a target <c>x</c> fronting a scheduler <c>y</c>:
    /// the key <c>x/y</c> resolves to the local scheduler, so its history is the local one too.
    /// </summary>
    [Test]
    public void ALocalSchedulerNamedLikeAKeyKeepsItsOwnHistory()
    {
        IScheduler local = A.Fake<IScheduler>();
        A.CallTo(() => local.SchedulerName).Returns("x/y");
        A.CallTo(() => local.SchedulerInstanceId).Returns("node-1");

        SchedulerRepository repository = new();
        repository.Bind(local);

        IExecutionHistoryStore shared = A.Fake<IExecutionHistoryStore>();
        IExecutionHistoryStore targetsOwn = A.Fake<IExecutionHistoryStore>();

        SchedulerTargets targets = new();
        targets.Add(new SchedulerTarget { Name = "x", Origin = SchedulerOrigin.Remote, History = (_, _) => targetsOwn });

        ServiceCollection services = new();
        services.AddSingleton<ISchedulerRepository>(repository);
        services.AddSingleton(targets);
        using ServiceProvider provider = services.BuildServiceProvider();

        IExecutionHistoryStore? found = ExecutionHistoryLookup.Find(provider, null, shared, "x/y", out string schedulerName, out string? refusal);

        found.Should().BeSameAs(shared,
            "the key resolves to the scheduler of this process, and reading target x's history for it would be a cross-scheduler read");
        schedulerName.Should().Be("x/y", "the store is asked for the whole name");
        refusal.Should().BeNull();

        ExecutionHistoryLookup.Find(provider, null, shared, "x/other", out schedulerName, out _).Should().BeSameAs(targetsOwn,
            "a key no local scheduler is named like is the target's scheduler");
        schedulerName.Should().Be("other");
    }
}
