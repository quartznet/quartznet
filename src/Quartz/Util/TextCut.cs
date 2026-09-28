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

namespace Quartz.Util;

/// <summary>
/// Cuts a text that is kept short to what its column holds.
/// </summary>
internal static class TextCut
{
    /// <summary>
    /// The longest start of <paramref name="value" /> that is at most <paramref name="maxLength" /> UTF-16
    /// code units and at most <paramref name="maxUtf8Bytes" /> bytes of UTF-8, ending on a whole
    /// character: never between the two halves of a surrogate pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A text that fits is answered as it is, the caller's own instance.
    /// </para>
    /// <para>
    /// The byte limit is for a column that counts bytes rather than characters, as Oracle's
    /// <c>VARCHAR2</c> does and Firebird's <c>VARCHAR</c> does in a database created without a character
    /// set. A lone surrogate counts as the three bytes of the replacement character an encoder writes in
    /// its place.
    /// </para>
    /// </remarks>
    internal static string? ToFit(string? value, int maxLength, int maxUtf8Bytes = int.MaxValue)
    {
        if (value is null)
        {
            return null;
        }

        // A UTF-16 code unit is at most three bytes of UTF-8, so the bytes only need counting when the
        // byte limit could bind.
        if (value.Length <= maxLength
            && (value.Length <= maxUtf8Bytes / 3 || Encoding.UTF8.GetByteCount(value) <= maxUtf8Bytes))
        {
            return value;
        }

        ReadOnlySpan<char> rest = value;
        int length = 0;
        int bytes = 0;

        while (!rest.IsEmpty)
        {
            Rune.DecodeFromUtf16(rest, out Rune rune, out int units);

            if (length + units > maxLength || bytes + rune.Utf8SequenceLength > maxUtf8Bytes)
            {
                break;
            }

            length += units;
            bytes += rune.Utf8SequenceLength;
            rest = rest[units..];
        }

        return value[..length];
    }
}
