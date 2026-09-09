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

using System;
using System.Threading.Tasks;

using FakeItEasy;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// Every duration the persistent store waits out on a timer is refused where it is configured, rather
/// than by the timer that eventually takes it.
/// </summary>
/// <remarks>
/// <c>Task.Delay</c> refuses a delay past its ceiling with an <see cref="ArgumentOutOfRangeException" />
/// naming a parameter called <c>delay</c>, from wherever the wait happened to be observed —
/// #3577's <c>MisfireHandlerFrequency</c> surfaced out of <c>Shutdown</c>, saying nothing about the
/// setting or the limit. This branch has no options validators, so each check lives on the property
/// setter that the matching flat <c>quartz.*</c> key writes.
/// </remarks>
public class ConfiguredWaitCeilingTest
{
    /// <summary>
    /// Well past the ceiling on every target framework: <c>net462</c> stops a little under 25 days and
    /// .NET 8 and later a little under 50.
    /// </summary>
    private static readonly TimeSpan LongerThanAnyTimerWillWait = TimeSpan.FromDays(60);

    [Test]
    public void AMisfireHandlerFrequencyNoTimerWillWaitOutIsRefused()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.MisfireHandlerFrequency = LongerThanAnyTimerWillWait;

        act.Should().Throw<ArgumentOutOfRangeException>(
                "the misfire handler sleeps for it between passes, and #3577 is what happens when it is not checked")
            .WithMessage("*MisfireHandlerFrequency*");
    }

    [Test]
    public void AClusterCheckinIntervalNoTimerWillWaitOutIsRefused()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.ClusterCheckinInterval = LongerThanAnyTimerWillWait;

        act.Should().Throw<ArgumentOutOfRangeException>("the cluster manager sleeps for it between check-ins")
            .WithMessage("*ClusterCheckinInterval*");
    }

    [Test]
    public void ADbRetryIntervalNoTimerWillWaitOutIsRefused()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.DbRetryInterval = LongerThanAnyTimerWillWait;

        act.Should().Throw<ArgumentOutOfRangeException>("the store waits it out after a database failure")
            .WithMessage("*DbRetryInterval*");
    }

    [Test]
    public void ATransientRetryIntervalNoTimerWillWaitOutIsRefused()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.TransientRetryInterval = LongerThanAnyTimerWillWait;

        act.Should().Throw<ArgumentOutOfRangeException>("it is waited out between the retries of a failed statement")
            .WithMessage("*TransientRetryInterval*");
    }

    /// <summary>
    /// The ceiling names the setting, the limit and the value, because none of the three is guessable
    /// from an exception raised by a timer half an application away.
    /// </summary>
    [Test]
    public void TheRefusalNamesTheSettingTheCeilingAndTheValue()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.DbRetryInterval = LongerThanAnyTimerWillWait;

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*must be at most*")
            .WithMessage("*" + TimerLimits.MaxDelay.TotalMilliseconds + "ms*")
            .WithMessage("*5184000000ms*");
    }

    /// <summary>
    /// A value at the ceiling is accepted: it is the longest wait a timer takes, not the first one it
    /// refuses, and a check that refused it would be the same bug the other way round.
    /// </summary>
    [Test]
    public void TheCeilingItselfIsStillAccepted()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport();

        Action act = () => store.DbRetryInterval = TimerLimits.MaxDelay;

        act.Should().NotThrow();
    }

    /// <summary>
    /// The misfire threshold is also the misfire handler's sleep when no frequency of its own is set,
    /// so it reaches the same timer. It is checked when the store is initialized rather than on its
    /// setter, because the two properties can be written in either order.
    /// </summary>
    [Test]
    public async Task AMisfireThresholdThatIsAlsoTheHandlerFrequencyIsRefusedAtStartup()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport
        {
            DataSource = "someDataSource",
            MisfireThreshold = LongerThanAnyTimerWillWait
        };

        Func<Task> act = () => store.Initialize(A.Fake<ITypeLoadHelper>(), A.Fake<ISchedulerSignaler>());

        (await act.Should().ThrowAsync<SchedulerConfigException>(
                "a threshold that is also the sleep has to be reported at startup, not out of Shutdown"))
            .WithMessage("*MisfireThreshold*")
            .WithMessage("*MisfireHandlerFrequency*",
                "the message has to say which setting makes the long threshold legal again");
    }

    /// <summary>
    /// With a frequency of its own set, the threshold is no longer a sleep, so a long one is not a
    /// mistake — a trigger that may be a hundred days late is a legitimate thing to want.
    /// </summary>
    [Test]
    public async Task AMisfireThresholdWithAFrequencyOfItsOwnIsLeftAlone()
    {
        JobStoreSupportTest.TestJobStoreSupport store = new JobStoreSupportTest.TestJobStoreSupport
        {
            DataSource = "someDataSource",
            MisfireHandlerFrequency = TimeSpan.FromMinutes(1),
            MisfireThreshold = LongerThanAnyTimerWillWait,
            DirectDelegate = A.Fake<IDriverDelegate>()
        };

        Func<Task> act = () => store.Initialize(A.Fake<ITypeLoadHelper>(), A.Fake<ISchedulerSignaler>());

        await act.Should().NotThrowAsync(
            "the threshold is only the handler's sleep when nothing else is, and here something else is");
    }

    /// <summary>
    /// The lock handlers' retry period has no options type and so nothing that validates it at startup.
    /// Left unchecked, a period past the ceiling is refused by the first contended lock attempt instead
    /// — with the lock unacquired and nothing naming the setting.
    /// </summary>
    [Test]
    public void ARetryPeriodNoTimerWillWaitOutIsRefusedWhereItIsConfigured()
    {
        IDbProvider provider = A.Fake<IDbProvider>();

        Action standard = () => new StdRowLockSemaphore("QRTZ_", "sched", null, provider)
        {
            RetryPeriod = LongerThanAnyTimerWillWait
        };
        standard.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RetryPeriod*");

        Action postgres = () => new PostgreSQLRowLockSemaphore("QRTZ_", "sched", null, provider)
        {
            RetryPeriod = LongerThanAnyTimerWillWait
        };
        postgres.Should().Throw<ArgumentOutOfRangeException>(
                "the PostgreSQL handler differs only in its insert statement, and inherits the period")
            .WithMessage("*RetryPeriod*");
    }

    [Test]
    public void ANegativeRetryPeriodIsRefusedTheSameWay()
    {
        IDbProvider provider = A.Fake<IDbProvider>();

        Action act = () => new StdRowLockSemaphore("QRTZ_", "sched", null, provider)
        {
            RetryPeriod = TimeSpan.FromSeconds(-1)
        };

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RetryPeriod must not be negative*");
    }
}
