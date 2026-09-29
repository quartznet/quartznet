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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What <see cref="IJobStore.AcquireNextTriggersAndFireDue" /> answers for a store that does not fire on
/// acquisition itself: everything it acquired, pending, for the scheduler to fire as it always has.
/// </summary>
public sealed class FireOnAcquireDefaultsTest
{
    private static readonly TriggerAcquisitionRequest request = new()
    {
        NoLaterThan = new DateTimeOffset(2031, 6, 17, 10, 0, 0, TimeSpan.Zero),
        MaxCount = 5,
    };

    /// <summary>
    /// A store written against 4.3 has no answer of its own, and the interface's default fires nothing: the
    /// scheduler goes on firing what the store acquired with <c>TriggersFired</c>, and handling that call's
    /// failures itself.
    /// </summary>
    [Test]
    public async Task AStoreWithoutItsOwnAnswerHandsBackEverythingItAcquiredAsPending()
    {
        IOperableTrigger trigger = Trigger("one");
        List<IOperableTrigger> acquired = [trigger];

        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.AcquireNextTriggers(request, A<CancellationToken>._)).Returns(acquired);
        A.CallTo(() => store.AcquireNextTriggersAndFireDue(A<TriggerAcquisitionRequest>._, A<CancellationToken>._)).CallsBaseMethod();

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(request);

        round.Pending.Should().BeSameAs(acquired, "the acquisition's own answer, which the scheduler copies as it always has");
        round.Due.Should().BeEmpty("the default fires nothing");
        round.Fired.Should().BeEmpty();
        A.CallTo(() => store.TriggersFired(A<IReadOnlyCollection<IOperableTrigger>>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    /// <summary>
    /// A decorator's override of <c>AcquireNextTriggers</c> keeps seeing every acquisition the scheduler
    /// makes: <see cref="DelegatingJobStore" /> answers through it, and the inner store never gets to
    /// acquire and fire behind it.
    /// </summary>
    [Test]
    public async Task ADecoratorsAcquisitionOverrideIsNotBypassed()
    {
        IOperableTrigger trigger = Trigger("one");
        IJobStore inner = A.Fake<IJobStore>();
        A.CallTo(() => inner.AcquireNextTriggers(A<TriggerAcquisitionRequest>._, A<CancellationToken>._)).Returns(new List<IOperableTrigger> { trigger });

        BudgetedJobStore decorator = new(inner, budget: 2);

        TriggerAcquisitionResult round = await decorator.AcquireNextTriggersAndFireDue(request);

        round.Pending.Should().Equal([trigger]);
        round.Due.Should().BeEmpty("a decorator fires nothing on acquisition unless it says so; the scheduler fires the pending ones");
        A.CallTo(() => inner.AcquireNextTriggers(A<TriggerAcquisitionRequest>.That.Matches(x => x.MaxCount == 2), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        // Forwarded, the inner store would acquire the whole request and fire it, past the budget.
        A.CallTo(() => inner.AcquireNextTriggersAndFireDue(A<TriggerAcquisitionRequest>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    /// <summary>
    /// A result that sets only what it has reads the rest as empty, which is how a store that fires nothing
    /// answers.
    /// </summary>
    [Test]
    public void AnUnsetPartOfTheResultIsEmpty()
    {
        TriggerAcquisitionResult round = new();

        round.Due.Should().BeEmpty();
        round.Fired.Should().BeEmpty();
        round.Pending.Should().BeEmpty();
    }

    private static IOperableTrigger Trigger(string name)
    {
        return (IOperableTrigger) TriggerBuilder.Create().WithIdentity(name, "defaults").ForJob("job", "defaults").StartNow().Build();
    }

    /// <summary>
    /// The documented shape of a decorator that narrows what its node acquires.
    /// </summary>
    private sealed class BudgetedJobStore(IJobStore inner, int budget) : DelegatingJobStore(inner)
    {
        public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
            TriggerAcquisitionRequest request,
            CancellationToken cancellationToken = default)
        {
            return base.AcquireNextTriggers(request with { MaxCount = Math.Min(request.MaxCount, budget) }, cancellationToken);
        }
    }
}
