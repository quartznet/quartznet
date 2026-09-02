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

using System.Reflection;

using FakeItEasy;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Every member of <see cref="IJobStore" /> and <see cref="IScheduler" /> is declared by the types
/// whose whole job is to hand it on.
/// </summary>
/// <remarks>
/// <para>
/// A default interface member is not inherited into a class's member set. So a forwarding type that
/// does not declare one lets the interface's own default body run <em>on the forwarder</em>, and
/// whatever that default decomposes into comes back through the forwarder as a different pair of
/// calls. Today every such default happens to decompose into members that are forwarded, so the
/// answer is right; the day one lands whose default cannot — a <c>throw</c>, a store-specific
/// shortcut, anything reading state the wrapper does not have — the decorator breaks over an inner
/// implementation that had the member all along, and nothing fails first.
/// </para>
/// <para>
/// This sweep is what fails first, and it has no exemption list on purpose: a member that genuinely
/// should not be forwarded has to be argued for by removing it from here, which is a conversation
/// rather than an omission.
/// </para>
/// </remarks>
public sealed class DelegatingForwardingTest
{
    private static IEnumerable<TestCaseData> Forwarders()
    {
        yield return new TestCaseData(typeof(DelegatingJobStore), typeof(IJobStore))
            .SetArgDisplayNames(nameof(DelegatingJobStore), nameof(IJobStore));

        yield return new TestCaseData(typeof(DelegatingScheduler), typeof(IScheduler))
            .SetArgDisplayNames(nameof(DelegatingScheduler), nameof(IScheduler));

        // Not a decorator, but the same hazard: it is the IScheduler a container hands out, so a member
        // it leaves to the interface default is one every injected scheduler answers by decomposition.
        yield return new TestCaseData(typeof(DeferredScheduler), typeof(IScheduler))
            .SetArgDisplayNames(nameof(DeferredScheduler), nameof(IScheduler));
    }

    [TestCaseSource(nameof(Forwarders))]
    public void EveryContractMemberIsDeclaredByTheForwardingType(Type forwarder, Type contract)
    {
        InterfaceMapping mapping = forwarder.GetInterfaceMap(contract);

        List<string> notForwarded = [];
        for (int i = 0; i < mapping.InterfaceMethods.Length; i++)
        {
            if (mapping.TargetMethods[i].DeclaringType != forwarder)
            {
                notForwarded.Add(Describe(mapping.InterfaceMethods[i]));
            }
        }

        notForwarded.Should().BeEmpty(
            $"{forwarder.Name} exists to hand every {contract.Name} call to the instance behind it, and a "
            + "member it does not declare is answered by the interface's default implementation running on "
            + $"{forwarder.Name} itself - which asks the inner instance a different question, or none at all");
    }

    /// <summary>
    /// And the member the sweep was written for actually forwards, rather than being declared and
    /// answered locally.
    /// </summary>
    [Test]
    public async Task ResettingAGroupOfTriggersInErrorIsHandedOnWithItsMatcher()
    {
        TriggerKey reset = new("trigger", "saga-17");

        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.ResetTriggersFromErrorState(A<GroupMatcher<TriggerKey>>._, A<CancellationToken>._))
            .Returns(new List<TriggerKey> { reset });

        DelegatingScheduler scheduler = new(inner);
        GroupMatcher<TriggerKey> matcher = GroupMatcher<TriggerKey>.GroupEquals("saga-17");

        (await scheduler.ResetTriggersFromErrorState(matcher)).Should().Equal([reset]);

        A.CallTo(() => inner.ResetTriggersFromErrorState(matcher, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => inner.QueryTriggers(A<TriggerQuery>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    private static string Describe(MethodInfo method)
    {
        return $"{method.DeclaringType!.Name}.{method.Name}({string.Join(", ", method.GetParameters().Select(x => x.ParameterType.Name))})";
    }
}
