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

using System.Runtime.InteropServices;

namespace Quartz.Util;

/// <summary>
/// Splits a string or span on one or two separator characters without allocating.
/// </summary>
/// <remarks>
/// The cron parser is the only caller, which is why this is not part of <c>StringExtensions</c>: the
/// trims there are read by the XML and JSON scheduling-data processors, and keeping the two apart is
/// what lets <c>Quartz.Analyzers</c> link this file without them and would let the cron closure move
/// into an assembly of its own. The name differs from <c>StringExtensions</c> for the reason
/// <see cref="CronThrow" />'s differs from <c>Throw</c>.
/// </remarks>
internal static class SpanSplitExtensions
{
    // based on https://www.meziantou.net/split-a-string-into-lines-without-allocation.htm
    internal static StringSplitEnumerator SpanSplit(this string str, char ch1, char ch2 = char.MinValue) => SpanSplit(str.AsSpan(), ch1, ch2);

    internal static StringSplitEnumerator SpanSplit(this ReadOnlySpan<char> span, char ch1, char ch2 = char.MinValue) => new(span, ch1, ch2);

    // Must be a ref struct as it contains a ReadOnlySpan<char>
    [StructLayout(LayoutKind.Auto)]
    internal ref struct StringSplitEnumerator
    {
        private ReadOnlySpan<char> _str;
        private readonly char ch1;
        private readonly char ch2;

        public StringSplitEnumerator(ReadOnlySpan<char> str, char ch1, char ch2)
        {
            _str = str;
            this.ch1 = ch1;
            this.ch2 = ch2;
            Current = default;
        }

        // Needed to be compatible with the foreach operator
        public StringSplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            ReadOnlySpan<char> span = _str;
            if (span.Length == 0) // Reach the end of the string
                return false;

            int index = ch2 != char.MinValue
                ? span.IndexOfAny(ch1, ch2)
                : span.IndexOf(ch1);

            if (index == -1) // The string is composed of only token
            {
                _str = ReadOnlySpan<char>.Empty; // The remaining string is an empty string
                Current = new StringSplitEntry(span, ReadOnlySpan<char>.Empty);
                return true;
            }

            Current = new StringSplitEntry(span.Slice(0, index), span.Slice(index, 1));
            _str = span.Slice(index + 1);
            return true;
        }

        public StringSplitEntry Current { get; private set; }
    }

    [StructLayout(LayoutKind.Auto)]
    internal readonly ref struct StringSplitEntry
    {
        public StringSplitEntry(ReadOnlySpan<char> token, ReadOnlySpan<char> separator)
        {
            Token = token;
            Separator = separator;
        }

        public ReadOnlySpan<char> Token { get; }
        public ReadOnlySpan<char> Separator { get; }

        // This method allow to deconstruct the type, so you can write any of the following code
        // foreach (var entry in str.SplitLines()) { _ = entry.Line; }
        // foreach (var (line, endOfLine) in str.SplitLines()) { _ = line; }
        // https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/deconstruct?WT.mc_id=DT-MVP-5003978#deconstructing-user-defined-types
        public void Deconstruct(out ReadOnlySpan<char> line, out ReadOnlySpan<char> separator)
        {
            line = Token;
            separator = Separator;
        }

        // This method allow to implicitly cast the type into a ReadOnlySpan<char>, so you can write the following code
        // foreach (ReadOnlySpan<char> entry in str.SplitLines())
        public static implicit operator ReadOnlySpan<char>(StringSplitEntry entry) => entry.Token;
    }
}
