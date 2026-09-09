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
using System.Threading;
using System.Threading.Tasks;

using Quartz.Util;

namespace Quartz.Tests.Unit.Util;

/// <summary>
/// Holds the ceiling every configured duration is checked against, and the absence of one where a
/// duration is waited out on something other than a timer, to what the primitives actually do.
/// </summary>
/// <remarks>
/// Neither fact is anything the BCL exposes, so without these the ceiling is a number copied out of a
/// runtime source file and the absence is an assumption. It is not even the same number on every
/// framework this package targets — .NET Framework's <c>Task.Delay</c> narrows to an <c>int</c>
/// millisecond count and stops at about 24.9 days, .NET 5 and later go on to about 49.7 — which is why
/// this runs on every target framework the test project has.
/// </remarks>
public class TimerLimitsTest
{
    /// <summary>
    /// A cancelled token, so that the calls below are validated without leaving a month-long timer
    /// behind in the test process.
    /// </summary>
    private static CancellationToken Cancelled()
    {
        CancellationTokenSource source = new CancellationTokenSource();
        source.Cancel();
        return source.Token;
    }

    [Test]
    public void MaxDelayIsTheLongestWaitATimerAccepts()
    {
        CancellationToken cancelled = Cancelled();

        Action atTheLimit = () => _ = Task.Delay(TimerLimits.MaxDelay, cancelled);
        atTheLimit.Should().NotThrow("the limit is the longest accepted delay, not the first refused one");

        Action pastIt = () => _ = Task.Delay(TimerLimits.MaxDelay + TimeSpan.FromMilliseconds(1), cancelled);
        pastIt.Should().Throw<ArgumentOutOfRangeException>(
                "this is the failure #3577 reported, and the checks exist so nobody meets it")
            .Which.ParamName.Should().Be("delay");
    }

    /// <summary>
    /// Why the scheduling loop's idle wait is bounded below and not above. It is spent on a semaphore
    /// rather than a timer, so that a scheduling change can cut it short, and a semaphore takes a
    /// timeout of any length — including ones no timer would.
    /// </summary>
    [Test]
    public void ASemaphoreTakesATimeoutNoTimerWould()
    {
        CancellationToken cancelled = Cancelled();
        using SemaphoreSlim semaphore = new SemaphoreSlim(initialCount: 1);

        Action wellPastTheTimerCeiling = () => _ = semaphore.WaitAsync(TimerLimits.MaxDelay, cancelled);

        wellPastTheTimerCeiling.Should().NotThrow(
            "the idle wait would need a ceiling of its own if this ever stopped being true");
    }

    [Test]
    public void AFailureNamesTheOptionTheCeilingAndTheValue()
    {
        string message = TimerLimits.TooLong("MisfireHandlerFrequency", TimeSpan.FromDays(90), TimeSpan.FromMilliseconds(4294967294), "Because.");

        message.Should().Be(
            "MisfireHandlerFrequency must be at most 4294967294ms (49.7 days), was 7776000000ms (90 days). Because.",
            "the millisecond counts are what a reader compares with a configuration file, and the days are what say why it is a mistake");
    }

    [Test]
    public void AWaitPastTheCeilingIsRefusedAndSaysWhatToLookAt()
    {
        Action pastIt = () => TimerLimits.EnsureWaitable(TimerLimits.MaxDelay + TimeSpan.FromDays(1), "SomeInterval", "It becomes a wait.");

        pastIt.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*SomeInterval*It becomes a wait.*")
            .Which.ParamName.Should().Be("SomeInterval",
                "the parameter the framework would have named is called 'delay', which is no help at all "
                + "to somebody holding a configuration file");
    }

    [Test]
    public void ANegativeWaitIsRefusedTheSameWay()
    {
        Action negative = () => TimerLimits.EnsureWaitable(TimeSpan.FromSeconds(-1), "SomeInterval", "It becomes a wait.");

        negative.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*SomeInterval must not be negative*");
    }

    [Test]
    public void TheCeilingItselfIsAccepted()
    {
        Action atTheLimit = () => TimerLimits.EnsureWaitable(TimerLimits.MaxDelay, "SomeInterval", "It becomes a wait.");

        atTheLimit.Should().NotThrow("a check that refused what the timer accepts would be the same bug the other way round");
    }
}
