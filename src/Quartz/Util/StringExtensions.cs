namespace Quartz.Util;

/// <summary>
/// Extension methods for <see cref="string" />.
/// </summary>
internal static class StringExtensions
{
    /// <summary>
    /// Allows null-safe trimming of string.
    /// </summary>
    internal static string? NullSafeTrim(this string? s)
    {
        return s?.Trim();
    }

    /// <summary>
    /// Trims string and if resulting string is empty, null is returned.
    /// </summary>
    internal static string? TrimEmptyToNull(this string? s)
    {
        if (s is null)
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
}