using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Quartz.Util;

/// <summary>
/// Bit manipulation helpers used by the cron expression engine to scan
/// bitmask-encoded field values without allocating or walking collections.
/// </summary>
/// <remarks>
/// The operations delegate to the hardware-accelerated
/// <c>System.Numerics.BitOperations</c>.
/// </remarks>
internal static class BitUtil
{
    /// <summary>
    /// Returns the number of trailing zero bits in <paramref name="value" />,
    /// i.e. the zero-based index of its lowest set bit. Returns 64 when
    /// <paramref name="value" /> is zero (matching <c>BitOperations</c>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TrailingZeroCount(ulong value)
    {
        return BitOperations.TrailingZeroCount(value);
    }

    /// <summary>
    /// Returns the number of set bits in <paramref name="value" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int PopCount(ulong value)
    {
        return BitOperations.PopCount(value);
    }

    /// <summary>
    /// Finds the smallest value present in <paramref name="bits" /> that is
    /// greater than or equal to <paramref name="start" />. This is the bitmask
    /// equivalent of locating the next allowed value in a sorted set, in O(1)
    /// and without allocation.
    /// </summary>
    /// <param name="bits">Bitmask where bit <c>i</c> set means value <c>i</c> is allowed.</param>
    /// <param name="start">Inclusive lower bound to search from.</param>
    /// <param name="min">The matched value, when one exists.</param>
    /// <returns><see langword="true" /> when a value &gt;= <paramref name="start" /> exists; otherwise <see langword="false" />.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetMinValueStartingFrom(ulong bits, int start, out int min)
    {
        // Callers only ever pass a value within the field's natural domain
        // (0-59, 0-23, 1-31, 1-12, 1-7), so start is always in [0, 63].
        Debug.Assert(start is >= 0 and <= 63, "start must be in [0, 63]");

        // Mask off everything below start, then the lowest remaining set bit is
        // the next allowed value.
        ulong atOrAbove = bits & (~0UL << start);
        if (atOrAbove != 0)
        {
            min = TrailingZeroCount(atOrAbove);
            return true;
        }

        min = 0;
        return false;
    }
}
