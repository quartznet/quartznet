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

namespace System.Numerics;

/// <summary>
/// The two <see cref="BitOperations" /> members the cron field bitmasks are scanned with, which
/// arrived in .NET Core 3.0.
/// </summary>
/// <remarks>
/// <c>Quartz.Util.BitUtil</c> is the single choke point for both — that is why it exists — so this is
/// two bodies rather than a habit. The hardware intrinsics the BCL dispatches to are not reachable
/// from netstandard2.0, so these are the software fallbacks: de Bruijn multiplication for the
/// trailing-zero count and the SWAR popcount, each answering exactly what the BCL member answers,
/// including 64 for a zero input.
/// </remarks>
internal static class BitOperations
{
    private static ReadOnlySpan<byte> TrailingZeroCountDeBruijn =>
    [
        00, 01, 28, 02, 29, 14, 24, 03,
        30, 22, 20, 15, 25, 17, 04, 08,
        31, 27, 13, 23, 21, 19, 16, 07,
        26, 12, 18, 06, 11, 05, 10, 09
    ];

    public static int TrailingZeroCount(ulong value)
    {
        uint low = (uint) value;
        if (low == 0)
        {
            uint high = (uint) (value >> 32);
            return high == 0 ? 64 : 32 + TrailingZeroCount(high);
        }

        return TrailingZeroCount(low);
    }

    private static int TrailingZeroCount(uint value)
    {
        // value is never zero here; the caller has already answered that case.
        return TrailingZeroCountDeBruijn[(int) (((value & (uint) -(int) value) * 0x077CB531u) >> 27)];
    }

    public static int PopCount(ulong value)
    {
        const ulong Mask01 = 0x0101010101010101ul;
        const ulong Mask0F = 0x0F0F0F0F0F0F0F0Ful;
        const ulong Mask33 = 0x3333333333333333ul;
        const ulong Mask55 = 0x5555555555555555ul;

        value -= (value >> 1) & Mask55;
        value = (value & Mask33) + ((value >> 2) & Mask33);
        value = (value + (value >> 4)) & Mask0F;

        return (int) ((value * Mask01) >> 56);
    }
}
