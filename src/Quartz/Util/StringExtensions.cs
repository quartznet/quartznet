using System.Globalization;
using System.Runtime.InteropServices;

namespace Quartz.Util;

/// <summary>
/// Extension methods for <see cref="string" />.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Allows null-safe trimming of string.
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string? NullSafeTrim(this string? s)
    {
        return s?.Trim();
    }

    /// <summary>
    /// Trims string and if resulting string is empty, null is returned.
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string? TrimEmptyToNull(this string s)
    {
        if (s == null)
        {
            return null;
        }

        s = s.Trim();

        if (s.Length == 0)
        {
            return null;
        }

        return s;
    }

    public static bool IsNullOrWhiteSpace(this string? s)
    {
        return s == null || s.Trim().Length == 0;
    }

    public static string FormatInvariant(this string s, params object?[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, s, args);
    }

    // based on https://www.meziantou.net/split-a-string-into-lines-without-allocation.htm
    public static StringSplitEnumerator SpanSplit(this string str, char ch1, char ch2 = char.MinValue) => SpanSplit(str.AsSpan(), ch1, ch2);

    public static StringSplitEnumerator SpanSplit(this ReadOnlySpan<char> span, char ch1, char ch2 = char.MinValue) => new(span, ch1, ch2);

    // Must be a ref struct as it contains a ReadOnlySpan<char>
    [StructLayout(LayoutKind.Auto)]
    public ref struct StringSplitEnumerator
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
            var span = _str;
            if (span.Length == 0) // Reach the end of the string
                return false;

            var index = ch2 != char.MinValue
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
    public readonly ref struct StringSplitEntry
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