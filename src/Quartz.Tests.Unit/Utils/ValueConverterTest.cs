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

using System.Reflection;

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// The coercion three callers share: the job factory binding a <see cref="JobDataMap" /> on the fire
/// path, <c>JobDataExpression</c> proving a value round-trips before the map stores it, and the
/// configuration binder writing a component's properties.
/// </summary>
/// <remarks>
/// These are the semantics #3341 called load-bearing, which is why they are pinned here rather than
/// left to whichever caller happens to exercise them. Everything below is answered either in front of
/// the <see cref="System.ComponentModel.TypeDescriptor" /> fallback or by it, and the split between
/// the two is the only thing that changed when <c>ObjectUtils</c> was dissolved.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class ValueConverterTest
{
    [Test]
    public void AMissingValueBecomesTheTargetTypesDefault()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(int), null)
            .Should().Be(0, "a value type's setter cannot be handed null, so the default stands in for a missing value");

        ValueConverter.ConvertValueIfNecessary(typeof(string), null)
            .Should().BeNull("a reference type takes null as it is");
    }

    [Test]
    public void AValueWithNoRouteToTheTargetIsRefused()
    {
        Action converting = () => ValueConverter.ConvertValueIfNecessary(typeof(int), new DirtyFlagMap<int, string>());

        converting.Should().Throw<NotSupportedException>(
            "refusing is what lets JobDataExpression report an unbindable value at configuration time rather than at fire time");
    }

    [Test]
    public void AValueTheTargetAlreadyAcceptsIsPassedStraightThrough()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(IComparable), "test")
            .Should().Be("test", "the assignability check is in front of the converter, so most job data never reaches reflection at all");
    }

    [Test]
    public void AStringBecomesAnEnumMemberAndBack()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(DayOfWeek), "Wednesday")
            .Should().Be(DayOfWeek.Wednesday);

        ValueConverter.ConvertValueIfNecessary(typeof(string), DayOfWeek.Wednesday)
            .Should().Be("Wednesday");
    }

    [Test]
    public void ANumberAnEnumWasSerializedAsIsStillTheMember()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(DayOfWeek), 3)
            .Should().Be(DayOfWeek.Wednesday, "a JSON serializer is free to write an enum as its number, and reading one back has to find the member again");
    }

    [Test]
    public void NumbersWidenAndNarrow()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(double), 1234)
            .Should().Be(1234.0);

        ValueConverter.ConvertValueIfNecessary(typeof(int), 1234.5)
            .Should().Be(1234, "narrowing rounds, which is exactly the silent loss JobDataExpression's round-trip check exists to catch");
    }

    [Test]
    public void AStringBecomesATypeAndBack()
    {
        ValueConverter.ConvertValueIfNecessary(typeof(Type), "System.String")
            .Should().Be(typeof(string));

        ValueConverter.ConvertValueIfNecessary(typeof(string), typeof(string))
            .Should().Be("System.String");
    }

    [Test]
    public void APropertyWithNoParseRuleTakesWhateverTimeSpanParses()
    {
        PropertyInfo property = typeof(DurationHolder).GetProperty(nameof(DurationHolder.Default));

        ValueConverter.GetTimeSpanValueForProperty(property, "1")
            .Should().Be(TimeSpan.FromDays(1), "TimeSpan.Parse reads a bare number as whole days, and nothing here overrides that");
    }

    [TestCase(nameof(DurationHolder.Hours), 1, 60 * 60 * 1000)]
    [TestCase(nameof(DurationHolder.Minutes), 1, 60 * 1000)]
    [TestCase(nameof(DurationHolder.Seconds), 1, 1000)]
    [TestCase(nameof(DurationHolder.Milliseconds), 1, 1)]
    public void APropertyWithAParseRuleReadsABareNumberInTheseUnits(string propertyName, long value, long expectedMilliseconds)
    {
        PropertyInfo property = typeof(DurationHolder).GetProperty(propertyName);

        ValueConverter.GetTimeSpanValueForProperty(property, value)
            .Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds),
                "quartz.* keys spell a duration as a bare number and let [TimeSpanParseRule] say what the number means");
    }

    private sealed class DurationHolder
    {
        [TimeSpanParseRule(TimeSpanParseRule.Hours)]
        public TimeSpan Hours { get; set; }

        [TimeSpanParseRule(TimeSpanParseRule.Minutes)]
        public TimeSpan Minutes { get; set; }

        [TimeSpanParseRule(TimeSpanParseRule.Seconds)]
        public TimeSpan Seconds { get; set; }

        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan Milliseconds { get; set; }

        public TimeSpan Default { get; set; }
    }
}
