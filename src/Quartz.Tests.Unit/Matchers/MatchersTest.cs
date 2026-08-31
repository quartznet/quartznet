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

namespace Quartz.Tests.Unit.Matchers;

// declared inside the namespace so it wins over the test namespace's own "Matchers" segment
using Matchers = Quartz.Matchers;

public class MatchersTest
{
    [Test]
    public void AllJobsMatchesEveryJobKey()
    {
        EverythingMatcher<JobKey> matcher = Matchers.AllJobs();

        matcher.IsMatch(new JobKey("a")).Should().BeTrue();
        matcher.IsMatch(new JobKey("b", "group")).Should().BeTrue();
        matcher.Should().Be(EverythingMatcher<JobKey>.All(), "every everything-matcher of one key type is the same matcher");
    }

    [Test]
    public void AllTriggersMatchesEveryTriggerKey()
    {
        EverythingMatcher<TriggerKey> matcher = Matchers.AllTriggers();

        matcher.IsMatch(new TriggerKey("a")).Should().BeTrue();
        matcher.IsMatch(new TriggerKey("b", "group")).Should().BeTrue();
    }

    /// <summary>
    /// The arity-free <see cref="NameMatcher" /> is the same four comparisons as
    /// <see cref="NameMatcher{TKey}" />, over a name that belongs to no key.
    /// </summary>
    [TestCase("holiday-xmas", true)]
    [TestCase("holiday", false)]
    [TestCase("workday", false)]
    public void TheUntypedNameMatcherComparesABareName(string name, bool expected)
    {
        NameMatcher.NameStartsWith("holiday-").IsMatch(name).Should().Be(expected);
    }

    [Test]
    public void TheUntypedNameMatcherIsAValue()
    {
        NameMatcher.NameEquals("workday").Should().Be(NameMatcher.NameEquals("workday"),
            "a matcher is compared by the shape it was built with, not by its identity — a query record "
            + "carries one, and two equal records have to be equal");

        NameMatcher.NameEquals("workday").Should().NotBe(NameMatcher.NameStartsWith("workday"),
            "the comparison is part of the shape");

        NameMatcher.NameEquals("workday").GetHashCode().Should().Be(NameMatcher.NameEquals("workday").GetHashCode());
    }

    [TestCase("holiday-xmas")]
    [TestCase("workday")]
    public void TheUntypedNameMatcherReadsTheWholeNameForEachComparison(string name)
    {
        NameMatcher.NameEquals(name).IsMatch(name).Should().BeTrue();
        NameMatcher.NameStartsWith(name[..3]).IsMatch(name).Should().BeTrue();
        NameMatcher.NameEndsWith(name[^3..]).IsMatch(name).Should().BeTrue();
        NameMatcher.NameContains(name[1..^1]).IsMatch(name).Should().BeTrue();
    }

    [Test]
    public void KeyMatchesTheCompleteKey()
    {
        JobKey jobKey = new("name", "group");
        KeyMatcher<JobKey> jobMatcher = Matchers.Key(jobKey);
        jobMatcher.IsMatch(new JobKey("name", "group")).Should().BeTrue();
        jobMatcher.IsMatch(new JobKey("name", "other")).Should().BeFalse("the group is part of the key");

        TriggerKey triggerKey = new("name", "group");
        KeyMatcher<TriggerKey> triggerMatcher = Matchers.Key(triggerKey);
        triggerMatcher.IsMatch(new TriggerKey("name", "group")).Should().BeTrue();
        triggerMatcher.IsMatch(new TriggerKey("other", "group")).Should().BeFalse();
    }

    [Test]
    public void GroupComparesTheGroupWithTheGivenOperator()
    {
        Matchers.Group<JobKey>(StringOperator.Equality, "reporting").IsMatch(new JobKey("any", "reporting")).Should().BeTrue();
        Matchers.Group<JobKey>(StringOperator.Equality, "reporting").IsMatch(new JobKey("any", "reports")).Should().BeFalse();
        Matchers.Group<TriggerKey>(StringOperator.StartsWith, "rep").IsMatch(new TriggerKey("any", "reporting")).Should().BeTrue();
        Matchers.Group<TriggerKey>(StringOperator.Contains, "ort").IsMatch(new TriggerKey("any", "reporting")).Should().BeTrue();

        Matchers.Group<JobKey>(StringOperator.Equality, "reporting").Should().Be(
            GroupMatcher<JobKey>.GroupEquals("reporting"),
            "the entry class builds the same matcher the per-type factories do");
    }

    [Test]
    public void NameComparesTheNameWithTheGivenOperator()
    {
        Matchers.Name<JobKey>(StringOperator.EndsWith, "cleanup").IsMatch(new JobKey("nightly-cleanup", "any")).Should().BeTrue();
        Matchers.Name<JobKey>(StringOperator.EndsWith, "cleanup").IsMatch(new JobKey("cleanup-nightly", "any")).Should().BeFalse();

        Matchers.Name<TriggerKey>(StringOperator.Equality, "t1").Should().Be(
            NameMatcher<TriggerKey>.NameEquals("t1"),
            "the entry class builds the same matcher the per-type factories do");
    }

    [Test]
    public void CombinatorsChainLeftToRight()
    {
        IMatcher<JobKey> matcher = Matchers.Group<JobKey>(StringOperator.Equality, "reporting")
            .And(Matchers.Name<JobKey>(StringOperator.StartsWith, "daily"));

        matcher.IsMatch(new JobKey("daily-report", "reporting")).Should().BeTrue();
        matcher.IsMatch(new JobKey("weekly-report", "reporting")).Should().BeFalse();
        matcher.IsMatch(new JobKey("daily-report", "imports")).Should().BeFalse();

        IMatcher<JobKey> either = Matchers.Group<JobKey>(StringOperator.Equality, "reporting")
            .Or(Matchers.Group<JobKey>(StringOperator.Equality, "imports"));

        either.IsMatch(new JobKey("any", "reporting")).Should().BeTrue();
        either.IsMatch(new JobKey("any", "imports")).Should().BeTrue();
        either.IsMatch(new JobKey("any", "exports")).Should().BeFalse();

        IMatcher<JobKey> negated = either.Not();
        negated.IsMatch(new JobKey("any", "exports")).Should().BeTrue();
        negated.IsMatch(new JobKey("any", "imports")).Should().BeFalse();
    }

    [Test]
    public void StringOperatorNamesDiscriminateTheBuiltInOperators()
    {
        StringOperator.Equality.Name.Should().Be("Equality");
        StringOperator.StartsWith.Name.Should().Be("StartsWith");
        StringOperator.EndsWith.Name.Should().Be("EndsWith");
        StringOperator.Contains.Name.Should().Be("Contains");
        StringOperator.Anything.Name.Should().Be("Anything");

        StringOperator.Equality.ToString().Should().Be("Equality");
    }
}
