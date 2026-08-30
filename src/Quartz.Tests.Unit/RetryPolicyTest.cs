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

using System.Collections.Immutable;

namespace Quartz.Tests.Unit;

/// <summary>
/// What the three factories accept, what they refuse, and what waits they hand out.
/// </summary>
[TestFixture]
public class RetryPolicyTest
{
    [Test]
    public void FixedRepeatsTheOneDelay()
    {
        RetryPolicy policy = RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30));

        policy.MaxAttempts.Should().Be(3);
        policy.InitialDelay.Should().Be(TimeSpan.FromSeconds(30));
        policy.BackoffFactor.Should().Be(1, "a fixed policy does not back off");
        policy.MaxDelay.Should().BeNull();
        policy.Delays.Should().BeEmpty("a computed policy carries no delay table");

        policy.DelayFor(1).Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayFor(3).Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void ExponentialMultipliesEachDelayByTheFactor()
    {
        RetryPolicy policy = RetryPolicy.Exponential(4, TimeSpan.FromSeconds(10));

        policy.BackoffFactor.Should().Be(2, "two is the default factor");
        policy.DelayFor(1).Should().Be(TimeSpan.FromSeconds(10));
        policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(20));
        policy.DelayFor(3).Should().Be(TimeSpan.FromSeconds(40));
        policy.DelayFor(4).Should().Be(TimeSpan.FromSeconds(80));
    }

    [Test]
    public void ExponentialClampsToTheMaximumDelay()
    {
        RetryPolicy policy = RetryPolicy.Exponential(5, TimeSpan.FromSeconds(10), factor: 3, maxDelay: TimeSpan.FromMinutes(1));

        policy.DelayFor(1).Should().Be(TimeSpan.FromSeconds(10));
        policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayFor(3).Should().Be(TimeSpan.FromMinutes(1), "90 seconds is past the ceiling");
        policy.DelayFor(4).Should().Be(TimeSpan.FromMinutes(1));
    }

    [Test]
    public void ExponentialSaturatesInsteadOfOverflowingTheTimeSpan()
    {
        RetryPolicy policy = RetryPolicy.Exponential(2000, TimeSpan.FromDays(1), factor: 10);

        policy.DelayFor(1000).Should().Be(TimeSpan.MaxValue,
            "a wait that outgrows a TimeSpan saturates; overflowing to a negative delay would schedule the retry into the past");
    }

    [Test]
    public void ExplicitWalksTheTableAndThenRepeatsItsLastEntry()
    {
        RetryPolicy policy = RetryPolicy.Explicit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        policy.MaxAttempts.Should().Be(3, "the table's length is the attempt count");
        policy.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        policy.Delays.Should().Equal(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        policy.DelayFor(1).Should().Be(TimeSpan.FromSeconds(1));
        policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(5));
        policy.DelayFor(3).Should().Be(TimeSpan.FromSeconds(30));
        policy.DelayFor(4).Should().Be(TimeSpan.FromSeconds(30),
            "the last entry repeats, so arithmetic past the last attempt still answers rather than throwing");
    }

    [Test]
    public void ExplicitAcceptsACollectionAsWellAsAnArgumentList()
    {
        List<TimeSpan> delays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

        RetryPolicy.Explicit(delays).Should().Be(RetryPolicy.Explicit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)),
            "params takes an existing list, and the policy is the same value either way");
    }

    [Test]
    public void DelaysIsNeverTheDefaultImmutableArray()
    {
        RetryPolicy.Fixed(1, TimeSpan.Zero).Delays.IsDefault.Should().BeFalse(
            "an uninitialized ImmutableArray throws on every member, so a computed policy carries the empty one");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void APolicyThatNeverRetriesIsRefused(int maxAttempts)
    {
        Action fixedPolicy = () => RetryPolicy.Fixed(maxAttempts, TimeSpan.FromSeconds(1));
        fixedPolicy.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxAttempts");

        Action exponential = () => RetryPolicy.Exponential(maxAttempts, TimeSpan.FromSeconds(1));
        exponential.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxAttempts");
    }

    [Test]
    public void ANegativeDelayIsRefused()
    {
        Action fixedPolicy = () => RetryPolicy.Fixed(1, TimeSpan.FromSeconds(-1));
        fixedPolicy.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delay");

        Action exponential = () => RetryPolicy.Exponential(1, TimeSpan.FromSeconds(-1));
        exponential.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("initialDelay");

        Action explicitPolicy = () => RetryPolicy.Explicit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-1));
        explicitPolicy.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("delays");
    }

    [TestCase(0.5)]
    [TestCase(0)]
    [TestCase(-2)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void ABackoffThatIsNotABackoffIsRefused(double factor)
    {
        Action act = () => RetryPolicy.Exponential(3, TimeSpan.FromSeconds(1), factor);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("factor");
    }

    [Test]
    public void AFactorOfExactlyOneIsAllowedAndWaitsTheSameEveryTime()
    {
        RetryPolicy policy = RetryPolicy.Exponential(3, TimeSpan.FromSeconds(7), factor: 1);

        policy.DelayFor(1).Should().Be(TimeSpan.FromSeconds(7));
        policy.DelayFor(3).Should().Be(TimeSpan.FromSeconds(7), "a backoff of one never grows");
    }

    [Test]
    public void TheShapeIsPartOfTheValueEvenWhenTheWaitsMatch()
    {
        RetryPolicy backoff = RetryPolicy.Exponential(3, TimeSpan.FromSeconds(7), factor: 1);
        RetryPolicy flat = RetryPolicy.Fixed(3, TimeSpan.FromSeconds(7));

        backoff.Should().NotBe(flat,
            "the two hand out the same waits, but one says 'back off, at this rate' and the other says 'wait this long' - "
            + "and turning the rate up is a change to make on the first and not on the second");
    }

    [Test]
    public void ACeilingBelowTheFirstWaitIsRefused()
    {
        Action act = () => RetryPolicy.Exponential(3, TimeSpan.FromMinutes(5), maxDelay: TimeSpan.FromMinutes(1));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxDelay",
            "a ceiling under the initial delay would silently replace every wait with the ceiling");
    }

    [Test]
    public void AnEmptyDelayTableIsRefused()
    {
        Action act = () => RetryPolicy.Explicit();

        act.Should().Throw<ArgumentException>().WithParameterName("delays");
    }

    [Test]
    public void ANullDelayTableIsRefused()
    {
        Action act = () => RetryPolicy.Explicit(null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("delays");
    }

    [Test]
    public void ADelayTableTooLongForTheColumnIsRefused()
    {
        ImmutableArray<TimeSpan> delays = [.. Enumerable.Repeat(TimeSpan.FromMinutes(1), 30)];

        Action act = () => RetryPolicy.Explicit(delays);

        act.Should().Throw<ArgumentException>().WithParameterName("delays").WithMessage("*250*",
            "RETRY_POLICY is 250 characters wide in every dialect, so an oversized policy has to fail where it is built rather than where it is inserted");
    }

    [Test]
    public void AnAttemptBelowOneIsRefused()
    {
        RetryPolicy policy = RetryPolicy.Fixed(3, TimeSpan.FromSeconds(1));

        Action act = () => policy.DelayFor(0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("attempt",
            "attempts count from one: DelayFor(1) is the wait after the first failure");
    }

    [Test]
    public void EqualityComparesTheWaitsAndNotTheReference()
    {
        RetryPolicy left = RetryPolicy.Explicit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        RetryPolicy right = RetryPolicy.Explicit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        left.Should().Be(right, "a policy is a value; the delay table is compared entry by entry");
        left.GetHashCode().Should().Be(right.GetHashCode());
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();

        left.Should().NotBe(RetryPolicy.Explicit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)));
        left.Should().NotBe(RetryPolicy.Fixed(2, TimeSpan.FromSeconds(1)),
            "a table that happens to start with the fixed delay is still a different policy");
    }

    [Test]
    public void EqualityHandlesNull()
    {
        RetryPolicy policy = RetryPolicy.Fixed(1, TimeSpan.FromSeconds(1));

        policy.Equals(null).Should().BeFalse();
        (policy == null).Should().BeFalse();
        (null == policy).Should().BeFalse();
        ((RetryPolicy) null == null).Should().BeTrue();
        policy.Equals((object) "not a policy").Should().BeFalse();
    }

    [Test]
    public void TheCeilingIsPartOfTheValue()
    {
        RetryPolicy bounded = RetryPolicy.Exponential(3, TimeSpan.FromSeconds(1), 2, TimeSpan.FromSeconds(3));
        RetryPolicy unbounded = RetryPolicy.Exponential(3, TimeSpan.FromSeconds(1), 2);

        bounded.Should().NotBe(unbounded, "the two hand out different waits from the third attempt onwards");
    }
}
