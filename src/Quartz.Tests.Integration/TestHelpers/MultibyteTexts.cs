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

namespace Quartz.Tests.Integration.TestHelpers;

/// <summary>
/// What a multibyte text sent to a column the store cuts is made of.
/// </summary>
public enum MultibyteText
{
    /// <summary>
    /// <c>é</c>, two bytes of UTF-8 a character, exactly the column's length.
    /// </summary>
    TwoByte,

    /// <summary>
    /// <c>日</c>, three bytes of UTF-8 a character, exactly the column's length.
    /// </summary>
    ThreeByte,

    /// <summary>
    /// <c>😀</c>, four bytes of UTF-8 and a surrogate pair each, exactly the column's length in UTF-16
    /// code units.
    /// </summary>
    FourByte,

    /// <summary>
    /// <c>aé日😀</c> repeated past the column's length, so a cut can fall inside any of them.
    /// </summary>
    Mixed,

    /// <summary>
    /// ASCII to one short of the column's length, then a surrogate pair that straddles it.
    /// </summary>
    PairAcrossTheLimit,
}

/// <summary>
/// Builds the texts of <see cref="MultibyteText" />, and says independently of the store what a column
/// keeps of one.
/// </summary>
public static class MultibyteTexts
{
    private const string Emoji = "\U0001F600";

    /// <summary>Every kind, for a <c>TestCaseSource</c>.</summary>
    public static readonly MultibyteText[] Kinds = Enum.GetValues<MultibyteText>();

    /// <summary>A text of <paramref name="kind" /> for a column <paramref name="length" /> characters long.</summary>
    public static string Of(MultibyteText kind, int length)
    {
        return kind switch
        {
            MultibyteText.TwoByte => new string('é', length),
            MultibyteText.ThreeByte => new string('日', length),
            MultibyteText.FourByte => string.Concat(Enumerable.Repeat(Emoji, length / 2)),
            MultibyteText.Mixed => Repeat("aé日" + Emoji, length + 10),
            MultibyteText.PairAcrossTheLimit => new string('x', length - 1) + Emoji + "tail",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>
    /// The longest start of <paramref name="text" /> that is at most <paramref name="length" /> UTF-16 code
    /// units and, where the column counts bytes, at most <paramref name="byteWidth" /> bytes of UTF-8, ending
    /// on a whole character.
    /// </summary>
    public static string Kept(string text, int length, int? byteWidth)
    {
        StringBuilder kept = new(length);
        int bytes = 0;

        foreach (Rune rune in text.EnumerateRunes())
        {
            if (kept.Length + rune.Utf16SequenceLength > length || bytes + rune.Utf8SequenceLength > (byteWidth ?? int.MaxValue))
            {
                break;
            }

            kept.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }

        return kept.ToString();
    }

    private static string Repeat(string piece, int length)
    {
        StringBuilder text = new(length + piece.Length);
        while (text.Length < length)
        {
            text.Append(piece);
        }

        return text.ToString();
    }
}
