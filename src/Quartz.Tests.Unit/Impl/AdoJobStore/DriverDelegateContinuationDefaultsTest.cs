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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a driver delegate written before 4.2 does when the store asks it about continuations.
/// </summary>
/// <remarks>
/// The three members are default interface members because <see cref="IDriverDelegate" /> is public
/// and a delegate of somebody else's must keep working — but the defaults are not all the same shape,
/// and which is which is the decision worth pinning. A delegate that cannot see the columns reports
/// nothing and fixes nothing, both of which are true for it; the one member that <em>refuses</em> is
/// the one only reachable through an answer the first member cannot give.
/// </remarks>
public sealed class DriverDelegateContinuationDefaultsTest
{
    private static readonly TriggerKey key = new TriggerKey("t", "g");

    [Test]
    public async Task ADelegateWithoutTheColumnsReportsNothingAwaiting()
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.SelectAwaitingContinuations(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .CallsBaseMethod();

        List<AwaitingContinuation> awaiting = await driverDelegate.SelectAwaitingContinuations(conn: null!, key);

        awaiting.Should().BeEmpty(
            "a delegate that never writes the continuation columns has no row to find — failing every "
            + "completion instead would break a store that was working perfectly well");
    }

    [Test]
    public async Task ADelegateWithoutTheColumnsHasNoFireTimeToFix()
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.ResetContinuationFireTime(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .CallsBaseMethod();

        int updated = await driverDelegate.ResetContinuationFireTime(conn: null!, key, DateTimeOffset.UtcNow);

        updated.Should().Be(0,
            "this runs on every reset from the error state, so a default that threw would break resetting "
            + "an ordinary trigger");
    }

    [Test]
    public async Task ADelegateThatFoundAContinuationAndCannotReleaseItSaysSo()
    {
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.ReleaseContinuation(
                A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<Quartz.Extensibility.StoredTriggerState>._, A<DateTimeOffset>._, A<CancellationToken>._))
            .CallsBaseMethod();

        Func<Task> release = async () => await driverDelegate.ReleaseContinuation(
            conn: null!, key, Quartz.Extensibility.StoredTriggerState.Waiting, DateTimeOffset.UtcNow);

        await release.Should().ThrowAsync<NotSupportedException>(
            "this is only reached for a continuation the select above found, so a delegate that answers one "
            + "and not the other is half-implemented rather than merely old");
    }
}
