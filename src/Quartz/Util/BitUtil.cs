using System.Diagnostics;
using System.Runtime.CompilerServices;
#if NET8_0_OR_GREATER
using System.Numerics;
#endif

namespace Quartz.Util;

/// <summary>
/// Bit manipulation helpers used by the cron expression engine to scan
/// bitmask-encoded field values without allocating or walking collections.
/// </summary>
/// <remarks>
/// On modern runtimes the operations delegate to the hardware-accelerated
/// <c>System.Numerics.BitOperations</c>. On frameworks that do not expose it
/// (net462, net472, netstandard2.0) a De Bruijn sequence lookup is used as a
/// portable fallback.
/// </remarks>
internal static class BitUtil
{
#if !NET8_0_OR_GREATER
    // De Bruijn sequence B(2, 6) and the matching lookup table for the index of
    // the lowest set bit. See https://www.chessprogramming.org/BitScan.
    private const ulong DeBruijn64 = 0x03f79d71b4cb0a89UL;

    private static readonly int[] DeBruijnPositions =
    [
        0, 1, 48, 2, 57, 49, 28, 3,
        61, 58, 50, 42, 38, 29, 17, 4,
        62, 55, 59, 36, 53, 51, 43, 22,
        45, 39, 33, 30, 24, 18, 12, 5,
        63, 47, 56, 27, 60, 41, 37, 16,
        54, 35, 52, 21, 44, 32, 23, 11,
        46, 26, 40, 15, 34, 20, 31, 10,
        25, 14, 19, 9, 13, 8, 7, 6
    ];
#endif

    /// <summary>
    /// Returns the number of trailing zero bits in <paramref name="value" />,
    /// i.e. the zero-based index of its lowest set bit. Returns 64 when
    /// <paramref name="value" /> is zero (matching <c>BitOperations</c>).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int TrailingZeroCount(ulong value)
    {
#if NET8_0_OR_GREATER
        return BitOperations.TrailingZeroCount(value);
#else
        if (value == 0)
        {
            return 64;
        }

        // Isolate the lowest set bit, multiply by the De Bruijn constant and
        // use the top 6 bits as an index into the position table.
        ulong isolated = value & (0UL - value);
        return DeBruijnPositions[(isolated * DeBruijn64) >> 58];
#endif
    }

    /// <summary>
    /// Returns the number of set bits in <paramref name="value" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int PopCount(ulong value)
    {
#if NET8_0_OR_GREATER
        return BitOperations.PopCount(value);
#else
        // SWAR popcount (Hacker's Delight).
        value -= (value >> 1) & 0x5555555555555555UL;
        value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
        value = (value + (value >> 4)) & 0x0f0f0f0f0f0f0f0fUL;
        return (int) ((value * 0x0101010101010101UL) >> 56);
#endif
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
