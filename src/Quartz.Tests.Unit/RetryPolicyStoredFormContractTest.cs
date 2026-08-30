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

using System.Globalization;

namespace Quartz.Tests.Unit;

/// <summary>
/// Pins the stored form of <see cref="RetryPolicy" /> — the string the <c>RETRY_POLICY</c> column
/// of <c>QRTZ_TRIGGERS</c>, a trigger's JSON and a 3.x-shaped blob all carry.
/// </summary>
/// <remarks>
/// The exact strings are the contract, not an implementation detail: a database written by one
/// version is read by the next, and by another node in a mixed cluster at the same moment. Changing
/// any string below changes what a stored policy means, so a failure here is a decision to make and
/// not a baseline to update.
/// </remarks>
[TestFixture]
public class RetryPolicyStoredFormContractTest
{
    /// <summary>
    /// Every shape, spelled out. The marker comes first so a reader — and a query — can tell the
    /// shapes apart without parsing the rest.
    /// </summary>
    public static IEnumerable<TestCaseData> StoredForms()
    {
        yield return new TestCaseData(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(30)), "fixed;3;00:00:30").SetName("fixed, seconds");
        yield return new TestCaseData(RetryPolicy.Fixed(1, TimeSpan.Zero), "fixed;1;00:00:00").SetName("fixed, no wait at all");
        yield return new TestCaseData(RetryPolicy.Fixed(2, TimeSpan.FromDays(1)), "fixed;2;1.00:00:00").SetName("fixed, over a day");
        yield return new TestCaseData(RetryPolicy.Fixed(2, TimeSpan.FromMilliseconds(1500)), "fixed;2;00:00:01.5000000").SetName("fixed, sub-second");
        yield return new TestCaseData(RetryPolicy.Exponential(5, TimeSpan.FromSeconds(10)), "exp;5;00:00:10;2").SetName("exponential, default factor");
        yield return new TestCaseData(RetryPolicy.Exponential(5, TimeSpan.FromSeconds(10), 1.5), "exp;5;00:00:10;1.5").SetName("exponential, fractional factor");
        yield return new TestCaseData(RetryPolicy.Exponential(5, TimeSpan.FromSeconds(10), 2, TimeSpan.FromMinutes(10)), "exp;5;00:00:10;2;00:10:00").SetName("exponential, with a ceiling");
        yield return new TestCaseData(RetryPolicy.Explicit(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)), "list;00:00:01;00:00:05;00:00:30").SetName("explicit table");
        yield return new TestCaseData(RetryPolicy.Explicit(TimeSpan.FromMinutes(2)), "list;00:02:00").SetName("explicit table of one");
    }

    [TestCaseSource(nameof(StoredForms))]
    public void TheStoredFormIsExactlyThis(RetryPolicy policy, string expected)
    {
        policy.ToStoredString().Should().Be(expected, "the stored form is a persistence contract, not a display string");
        policy.ToString().Should().Be(expected, "a policy has one representation, and it is the one the database holds");
    }

    [TestCaseSource(nameof(StoredForms))]
    public void TheStoredFormParsesBackToAnEqualPolicy(RetryPolicy policy, string expected)
    {
        RetryPolicy parsed = RetryPolicy.Parse(expected);

        parsed.Should().Be(policy);
        parsed.ToStoredString().Should().Be(expected, "parsing and storing are each other's inverse");
    }

    [Test]
    public void TheMarkerIsTheFactoryThatWasCalledAndNotAGuessAtTheNumbers()
    {
        RetryPolicy policy = RetryPolicy.Exponential(3, TimeSpan.FromSeconds(1), factor: 1);

        policy.ToStoredString().Should().Be("exp;3;00:00:01;1",
            "the shape is recorded, not worked back out of the numbers - deciding it would mean asking whether a double is exactly one");
        RetryPolicy.Parse(policy.ToStoredString()).Should().Be(policy,
            "a shape that survives the round trip is what lets the column say which policy a trigger was given");
        RetryPolicy.Parse(policy.ToStoredString()).Should().NotBe(RetryPolicy.Fixed(3, TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void TheCeilingIsStoredWithTheBackoffItBelongsTo()
    {
        RetryPolicy policy = RetryPolicy.Exponential(3, TimeSpan.FromSeconds(1), factor: 1, maxDelay: TimeSpan.FromSeconds(1));

        policy.ToStoredString().Should().Be("exp;3;00:00:01;1;00:00:01");
        RetryPolicy.Parse(policy.ToStoredString()).Should().Be(policy);
    }

    [Test]
    public void TheFactorRoundTripsToTheBit()
    {
        RetryPolicy policy = RetryPolicy.Exponential(3, TimeSpan.FromSeconds(1), Math.PI);

        RetryPolicy.Parse(policy.ToStoredString()).BackoffFactor.Should().Be(Math.PI,
            "the factor is written with the round-trip format, so no precision is lost in the column");
    }

    [Test]
    public void TheStoredFormIgnoresTheAmbientCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // fi-FI writes a decimal comma and would otherwise turn 1.5 into "1,5" - which the
            // separator would then split on the next machine to read the row.
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");

            RetryPolicy policy = RetryPolicy.Exponential(5, TimeSpan.FromSeconds(10), 1.5, TimeSpan.FromMinutes(10));

            policy.ToStoredString().Should().Be("exp;5;00:00:10;1.5;00:10:00");
            RetryPolicy.Parse("exp;5;00:00:10;1.5;00:10:00").Should().Be(policy);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public void TheStoredFormFitsTheColumn()
    {
        foreach (TestCaseData testCase in StoredForms())
        {
            ((string) testCase.Arguments[1]).Length.Should().BeLessThanOrEqualTo(250,
                "RETRY_POLICY is a 250-character column in every dialect");
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("nope;3;00:00:30")]
    [TestCase("fixed")]
    [TestCase("fixed;3")]
    [TestCase("fixed;3;00:00:30;extra")]
    [TestCase("fixed;three;00:00:30")]
    [TestCase("fixed;3;half a minute")]
    [TestCase("fixed;0;00:00:30")]
    [TestCase("fixed;3;-00:00:30")]
    [TestCase("exp;3;00:00:10")]
    [TestCase("exp;3;00:00:10;0.5")]
    [TestCase("exp;3;00:00:10;2;00:00:01")]
    [TestCase("exp;3;00:00:10;2;00:10:00;more")]
    [TestCase("list")]
    [TestCase("list;")]
    [TestCase("list;00:00:01;nonsense")]
    [TestCase("FIXED;3;00:00:30")]
    public void WhatIsNotAStoredPolicyIsRefused(string value)
    {
        RetryPolicy.TryParse(value, out RetryPolicy policy).Should().BeFalse();
        policy.Should().BeNull();

        Action act = () => RetryPolicy.Parse(value);
        act.Should().Throw<FormatException>().WithMessage($"*{value}*",
            "the message names the value that could not be read, because the value came out of somebody's database");
    }

    [Test]
    public void ANullStoredFormIsNotAPolicy()
    {
        RetryPolicy.TryParse(null, out RetryPolicy policy).Should().BeFalse(
            "a trigger row with no retry policy reads as null, and that is not a parse failure to report");
        policy.Should().BeNull();

        Action act = () => RetryPolicy.Parse(null);
        act.Should().Throw<ArgumentNullException>().WithParameterName("value");
    }

    [Test]
    public void ADelayTableThatOverflowsTheColumnCannotBeParsedEither()
    {
        string tooMany = "list" + string.Concat(Enumerable.Repeat(";00:01:00", 30));

        tooMany.Length.Should().BeGreaterThan(250);
        RetryPolicy.TryParse(tooMany, out RetryPolicy policy).Should().BeFalse(
            "parsing goes through the same factory that refuses to build a policy the column cannot hold");
        policy.Should().BeNull();
    }
}
