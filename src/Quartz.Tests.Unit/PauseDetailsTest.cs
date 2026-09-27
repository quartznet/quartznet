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
/// How a pause's details become what a store records: cut to the columns, and blank read as nothing.
/// </summary>
public sealed class PauseDetailsTest
{
    private static readonly DateTimeOffset instant = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void AStampKeepsWhatFitsAndTheInstantItIsGiven()
    {
        PauseInfo stamped = new PauseDetails { Reason = "maintenance", RequestedBy = "alice" }.Stamp(instant);

        stamped.Should().Be(new PauseInfo("maintenance", "alice", instant));
    }

    [Test]
    public void ATextAtItsLimitIsKeptWhole()
    {
        string reason = new('r', PauseDetails.MaxReasonLength);

        PauseDetails.Truncate(reason, PauseDetails.MaxReasonLength).Should().BeSameAs(reason,
            "a text that fits is the caller's text, untouched");
    }

    [Test]
    public void ALongerTextIsCutToTheLimit()
    {
        PauseDetails.Truncate(new string('u', PauseDetails.MaxRequestedByLength + 1), PauseDetails.MaxRequestedByLength)
            .Should().HaveLength(PauseDetails.MaxRequestedByLength);
    }

    [Test]
    public void TheCutNeverSplitsASurrogatePair()
    {
        string text = new string('r', 9) + "\U0001F600" + "rest";

        PauseDetails.Truncate(text, 10).Should().Be(new string('r', 9),
            "half an emoji is not a character any column can store faithfully, so the cut falls before it");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ABlankTextIsNothing(string? text)
    {
        PauseDetails.Truncate(text, PauseDetails.MaxReasonLength).Should().BeNull(
            "a reason of nothing but spaces says nothing, and a listing should not show an empty quote");
    }

    [Test]
    public void TheReasonlessDetailsSayNothing()
    {
        PauseDetails.None.Stamp(instant).Should().Be(new PauseInfo(null, null, instant));
    }
}
