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

namespace Quartz.Tests.Unit;

/// <summary>
/// Equality, hashing and parsing for <see cref="JobKey" /> and <see cref="TriggerKey" />.
/// </summary>
public class KeyTest
{
    [Test]
    public void EqualityIsByNameAndGroup()
    {
        new JobKey("name", "group").Equals(new JobKey("name", "group")).Should().BeTrue();
        new JobKey("name", "group").Equals(new JobKey("name", "other")).Should().BeFalse();
        new JobKey("name", "group").Equals(new JobKey("other", "group")).Should().BeFalse();
        new JobKey("name", "group").Equals((JobKey) null).Should().BeFalse();

        new TriggerKey("name").Equals(new TriggerKey("name")).Should().BeTrue();
    }

    [Test]
    public void DefaultEqualityComparerTakesTheTypedPath()
    {
        // IEquatable<JobKey> on the sealed type is what routes EqualityComparer<T>.Default away
        // from the object-comparer path the job stores paid for on every dictionary probe.
        EqualityComparer<JobKey>.Default.Equals(new JobKey("a", "g"), new JobKey("a", "g")).Should().BeTrue();
        EqualityComparer<JobKey>.Default.Equals(new JobKey("a", "g"), new JobKey("b", "g")).Should().BeFalse();
        EqualityComparer<TriggerKey>.Default.Equals(new TriggerKey("a", "g"), new TriggerKey("a", "g")).Should().BeTrue();

        typeof(IEquatable<JobKey>).IsAssignableFrom(typeof(JobKey)).Should().BeTrue();
        typeof(IEquatable<TriggerKey>).IsAssignableFrom(typeof(TriggerKey)).Should().BeTrue();
    }

    [Test]
    public void EqualKeysHashEqually()
    {
        new JobKey("name", "group").GetHashCode().Should().Be(new JobKey("name", "group").GetHashCode());

        JobKey key = new JobKey("name", "group");
        key.GetHashCode().Should().Be(key.GetHashCode(), "the cached hash must be stable");
    }

    [Test]
    public void TryParseIsTheInverseOfToString()
    {
        JobKey original = new JobKey("my.job", "DEFAULT");

        JobKey.TryParse(original.ToString(), out JobKey parsed).Should().BeTrue();

        parsed.Should().Be(original, "ToString composes '<group>.<name>' and parsing splits at the first '.'");
        parsed.Group.Should().Be("DEFAULT");
        parsed.Name.Should().Be("my.job");
    }

    [Test]
    public void TryParseSplitsAtTheFirstDot()
    {
        TriggerKey.TryParse("group.name.with.dots", out TriggerKey parsed).Should().BeTrue();

        parsed.Group.Should().Be("group");
        parsed.Name.Should().Be("name.with.dots");
    }

    [Test]
    public void TryParseRejectsAStringWithNoSeparator()
    {
        JobKey.TryParse("nodothere", out JobKey parsed).Should().BeFalse();
        parsed.Should().BeNull();

        JobKey.TryParse(null, out parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Test]
    public void ParseThrowsFormatExceptionForAStringWithNoSeparator()
    {
        Action act = () => JobKey.Parse("nodothere");

        act.Should().Throw<FormatException>().WithMessage("*<group>.<name>*");
    }

    [Test]
    public void KeysAreParsable()
    {
        ParseViaInterface<JobKey>("DEFAULT.job").Should().Be(new JobKey("job", "DEFAULT"));
        ParseViaInterface<TriggerKey>("group.trigger").Should().Be(new TriggerKey("trigger", "group"));
    }

    private static TKey ParseViaInterface<TKey>(string text) where TKey : IParsable<TKey>
    {
        return TKey.Parse(text, provider: null);
    }
}
