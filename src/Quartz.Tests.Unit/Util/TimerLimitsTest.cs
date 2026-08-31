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

using Quartz.Util;

namespace Quartz.Tests.Unit.Util;

/// <summary>
/// Holds the ceiling the options validators check durations against, and the absence of one where a
/// duration is waited out on something other than a timer, to what the primitives actually do.
/// </summary>
/// <remarks>
/// Neither fact is anything the BCL exposes, so without these the ceiling is a number copied out of a
/// runtime source file and the absence is an assumption. A framework that moved either would be found
/// by a scheduler that had been running for a month rather than by a build.
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

        Action atTheLimit = () => _ = Task.Delay(TimerLimits.MaxDelay, TimeProvider.System, cancelled);
        atTheLimit.Should().NotThrow("the limit is the longest accepted delay, not the first refused one");

        Action pastIt = () => _ = Task.Delay(TimerLimits.MaxDelay + TimeSpan.FromMilliseconds(1), TimeProvider.System, cancelled);
        pastIt.Should().Throw<ArgumentOutOfRangeException>(
                "this is the failure #3577 reported, and the validators exist so nobody meets it")
            .Which.ParamName.Should().Be("delay");
    }

    /// <summary>
    /// Why <c>IdleWaitTime</c> is bounded below and not above. It is spent on a semaphore rather than a
    /// timer, so that a scheduling change can cut it short, and a semaphore takes a timeout of any
    /// length — including ones no timer would.
    /// </summary>
    [Test]
    public void ASemaphoreTakesATimeoutNoTimerWould()
    {
        CancellationToken cancelled = Cancelled();
        using SemaphoreSlim semaphore = new SemaphoreSlim(initialCount: 1);

        Action wellPastTheTimerCeiling = () => _ = semaphore.WaitAsync(TimerLimits.MaxDelay * 1000, cancelled);

        wellPastTheTimerCeiling.Should().NotThrow(
            "the scheduling loop's idle wait would need a ceiling of its own if this ever stopped being true");
    }

    [Test]
    public void MaxDelayIsTheNumberTheFailureMessagesQuote()
    {
        TimerLimits.MaxDelay.TotalMilliseconds.Should().Be(uint.MaxValue - 1);
        TimerLimits.MaxDelay.TotalDays.Should().BeApproximately(49.7, 0.05, "the messages say 49.7 days");
    }

    [Test]
    public void AFailureNamesTheOptionTheCeilingAndTheValue()
    {
        string message = TimerLimits.TooLong("MisfireHandlerFrequency", TimeSpan.FromDays(90), TimerLimits.MaxDelay, "Because.");

        message.Should().Be(
            "MisfireHandlerFrequency must be at most 4294967294ms (49.7 days), was 7776000000ms (90 days). Because.",
            "the millisecond counts are what a reader compares with a configuration file, and the days are what say why it is a mistake");
    }
}
