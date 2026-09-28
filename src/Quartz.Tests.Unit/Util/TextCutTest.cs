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

using System.Text;

using Quartz.Util;

namespace Quartz.Tests.Unit.Util;

/// <summary>
/// The cut that keeps a text within what its column holds, in characters and, where the column counts
/// bytes, in bytes of UTF-8 (#3905).
/// </summary>
public class TextCutTest
{
    private const string Emoji = "\U0001F600";

    [Test]
    public void NothingIsNothing()
    {
        TextCut.ToFit(null, 10, 10).Should().BeNull();
    }

    [Test]
    public void ATextThatFitsIsTheCallersOwn()
    {
        string text = new('é', 10);

        TextCut.ToFit(text, 10).Should().BeSameAs(text, "a text at its limit needs no cut, so nothing is copied");
        TextCut.ToFit(text, 10, 20).Should().BeSameAs(text, "ten two-byte characters are exactly twenty bytes");
    }

    [Test]
    public void OneCharacterOverTheLengthIsCut()
    {
        TextCut.ToFit(new string('x', 11), 10).Should().Be(new string('x', 10));
    }

    [Test]
    public void OneByteOverTheWidthCutsTheCharacterThatDoesNotFit()
    {
        TextCut.ToFit(new string('é', 10), 10, 19).Should().Be(new string('é', 9),
            "the tenth character would take the text to twenty bytes, one past the column");
    }

    [TestCase('é', 2)]
    [TestCase('日', 3)]
    public void AColumnThatCountsBytesKeepsAsManyWholeCharactersAsFit(char character, int bytesEach)
    {
        string kept = TextCut.ToFit(new string(character, 250), 250, 250);

        kept.Should().Be(new string(character, 250 / bytesEach));
        Encoding.UTF8.GetByteCount(kept).Should().BeLessThanOrEqualTo(250, "that is all the column holds");
    }

    [Test]
    public void APairAcrossTheLengthIsLeftOutWhole()
    {
        string text = new string('x', 9) + Emoji + "tail";

        TextCut.ToFit(text, 10).Should().Be(new string('x', 9),
            "half a surrogate pair is not a character any column can store faithfully");
    }

    [Test]
    public void APairAcrossTheWidthIsLeftOutWhole()
    {
        string text = new string('x', 7) + Emoji;

        TextCut.ToFit(text, 10, 10).Should().Be(new string('x', 7),
            "the pair is four bytes, and only three are left after the seven");
    }

    [Test]
    public void PairsThatFitAreKept()
    {
        string text = string.Concat(Enumerable.Repeat(Emoji, 5));

        TextCut.ToFit(text, 10).Should().BeSameAs(text, "five pairs are exactly ten code units");
        TextCut.ToFit(text, 10, 12).Should().Be(string.Concat(Enumerable.Repeat(Emoji, 3)),
            "each pair is four bytes, so twelve bytes hold three of them");
    }

    [Test]
    public void ALoneSurrogateCountsAsTheReplacementCharacterAnEncoderWrites()
    {
        string text = "ab\uD83D";

        TextCut.ToFit(text, 10, 4).Should().Be("ab", "the lone half is written as three bytes, one past the four");
        TextCut.ToFit(text, 10, 5).Should().BeSameAs(text);
    }
}
