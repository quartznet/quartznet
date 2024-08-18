using System.Security.Cryptography;

namespace Quartz.Core;

internal static class QuartzRandom
{
    private static double NextDouble()
    {
        using RandomNumberGenerator random = RandomNumberGenerator.Create();
        Span<byte> buf = stackalloc byte[4];
        random.GetBytes(buf);
        return (double) BitConverter.ToUInt32(buf) / uint.MaxValue;
    }

    /// <summary>
    /// Random number generator
    /// </summary>
    /// <param name="maxValue"></param>
    /// <returns>int between 0 and maxValue</returns>
    public static int Next(int maxValue)
    {
        return Next(0, maxValue);
    }

    /// <summary>
    /// Random number generator
    /// </summary>
    /// <returns>a positive integer</returns>
    public static int Next()
    {
        return Next(0, int.MaxValue);
    }

    /// <summary>
    /// Random number generator
    /// </summary>
    /// <param name="minValue"></param>
    /// <param name="maxValue"></param>
    /// <returns>integer between minValue and maxValue</returns>
    public static int Next(int minValue, int maxValue)
    {
        if (maxValue <= minValue)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(maxValue), "maxValue must be larger then minValue");
        }

        long range = (long) maxValue - minValue;
        return (int) Math.Floor(NextDouble() * range) + minValue;
    }
}