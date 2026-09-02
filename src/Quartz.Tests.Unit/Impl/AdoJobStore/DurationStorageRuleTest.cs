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
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.  See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

#endregion

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The whole-milliseconds rule at its two doors: the check a trigger's write site makes first, and
/// the conversion every dialect's delegate performs, which is the backstop for a caller that does not.
/// </summary>
/// <remarks>
/// <see cref="SubMillisecondIntervalSqliteTest" /> proves the rule through a real store. This pins the
/// two members themselves — that a whole number of milliseconds passes both, that a null passes the
/// conversion, and that a finer value is refused by the conversion on its own, so a dialect that
/// bypasses the write-site check still cannot write a zero (#3673).
/// </remarks>
[TestFixture]
public sealed class DurationStorageRuleTest
{
    [Test]
    public void AWholeNumberOfMillisecondsIsStorable()
    {
        Action check = () => AdoJobStoreUtil.RequireStorableDuration(TimeSpan.FromMilliseconds(250), AdoConstants.ColumnRepeatInterval, new TriggerKey("t", "g"));

        check.Should().NotThrow("250 ms is exactly what the column keeps");
    }

    [Test]
    public void AFinerValueIsRefusedNamingTheTriggerAndTheColumn()
    {
        Action check = () => AdoJobStoreUtil.RequireStorableDuration(TimeSpan.FromTicks(1), AdoConstants.ColumnRepeatInterval, new TriggerKey("t", "g"));

        check.Should().Throw<ArgumentException>()
            .WithMessage("*'g.t'*")
            .WithMessage($"*{AdoConstants.ColumnRepeatInterval}*",
                "the message has to say which trigger and which column, because the alternative was a row stored as zero");
    }

    [Test]
    public void TheConversionKeepsWholeMillisecondsAndPassesNullThrough()
    {
        StdAdoDelegate adoDelegate = new StdAdoDelegate();

        adoDelegate.GetDbTimeSpanValue(TimeSpan.FromMilliseconds(1500)).Should().Be(1500L,
            "the column holds the millisecond count, and 1.5 s is a whole number of them");
        adoDelegate.GetDbTimeSpanValue(null).Should().BeNull("a null duration is a null column");
    }

    [Test]
    public void TheConversionRefusesWhatItCannotHoldExactly()
    {
        StdAdoDelegate adoDelegate = new StdAdoDelegate();

        Action convert = () => adoDelegate.GetDbTimeSpanValue(TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond + 1));

        convert.Should().Throw<ArgumentException>()
            .WithMessage("*whole milliseconds*",
                "truncating to a millisecond would read back as a different value; the delegate is the last door and refuses too");
    }
}
