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

namespace Quartz.Tests.Unit;

/// <summary>
/// The one rule for reading and writing a scheduler key, which a replacement <c>IQuartzApiClient</c>
/// relies on as much as the shipped one.
/// </summary>
public sealed class SchedulerRefTest
{
    [Test]
    public void ABareNameIsItsOwnKey()
    {
        SchedulerRef bare = new("reporting");

        bare.Key.Should().Be("reporting");
        bare.Target.Should().BeNull();
        bare.ToString().Should().Be("reporting", "the key is how a ref is spelled wherever one is shown");
    }

    [Test]
    public void ATargetedRefSpellsTargetSlashName()
    {
        SchedulerRef targeted = new("QuartzScheduler", "w1");

        targeted.Key.Should().Be("w1/QuartzScheduler");
    }

    [Test]
    public void ParseRoundTripsBothForms()
    {
        SchedulerRef.Parse("reporting").Should().Be(new SchedulerRef("reporting"));
        SchedulerRef.Parse("w1/QuartzScheduler").Should().Be(new SchedulerRef("QuartzScheduler", "w1"));
        SchedulerRef.Parse(new SchedulerRef("QuartzScheduler", "a+b+c").Key).Target.Should().Be("a+b+c",
            "a cluster's target round-trips like any other");
    }

    [Test]
    public void ParseSplitsAtTheFirstSeparatorBecauseASchedulerNameMayContainOne()
    {
        SchedulerRef parsed = SchedulerRef.Parse("a/b/c");

        parsed.Target.Should().Be("a", "a target never contains the separator");
        parsed.SchedulerName.Should().Be("b/c", "and a scheduler's name may");
    }

    [TestCase("/reporting")]
    [TestCase("reporting/")]
    [TestCase(" /reporting")]
    public void AKeyWhoseTargetHalfIsEmptyIsABareName(string key)
    {
        SchedulerRef parsed = SchedulerRef.Parse(key);

        parsed.Target.Should().BeNull("neither half of such a key could be a target");
        parsed.SchedulerName.Should().Be(key);
    }

    [Test]
    public void ATargetContainingTheSeparatorIsRefused()
    {
        Action act = () => _ = new SchedulerRef("reporting", "a/b");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*a/b*", "the key it would produce could not be read back");
    }

    [Test]
    public void TheValidationHoldsThroughWith()
    {
        SchedulerRef bare = new("reporting");

        Action target = () => _ = bare with { Target = "a/b" };
        Action name = () => _ = bare with { SchedulerName = " " };

        target.Should().Throw<ArgumentException>("a copy is validated the way a construction is");
        name.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A registration whose target is empty rather than null is one reached through no target, as its
    /// display name already says.
    /// </summary>
    [Test]
    public void AnEmptyTargetOnARegistrationIsNoTarget()
    {
        SchedulerRegistration registration = new("reporting", SchedulerOrigin.Container, SchedulerStatus.Running) { Target = "" };

        registration.Key.Should().Be("reporting");
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ABlankNameIsRefused(string name)
    {
        Action construct = () => _ = new SchedulerRef(name);
        Action parse = () => SchedulerRef.Parse(name);

        construct.Should().Throw<ArgumentException>();
        parse.Should().Throw<ArgumentException>();
    }
}
