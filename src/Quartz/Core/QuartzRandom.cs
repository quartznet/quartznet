using System.Security.Cryptography;

namespace Quartz.Core;

internal static class QuartzRandom
{
    /// <summary>
    /// A random number below <paramref name="maxValue" />, from the thread-safe shared generator.
    /// </summary>
    /// <param name="maxValue">The exclusive upper bound, which must not be negative.</param>
    /// <returns>int between 0 (inclusive) and maxValue (exclusive)</returns>
    public static int Next(int maxValue)
    {
        return Next(0, maxValue);
    }

    /// <summary>
    /// A random non-negative number, from the thread-safe shared generator.
    /// </summary>
    /// <returns>a positive integer</returns>
    public static int Next()
    {
        return Next(0, int.MaxValue);
    }

    /// <summary>
    /// A random number in a range, from the thread-safe shared generator.
    /// </summary>
    /// <param name="minValue">The inclusive lower bound.</param>
    /// <param name="maxValue">The exclusive upper bound, which must not be below <paramref name="minValue" />.</param>
    /// <returns>integer between minValue (inclusive) and maxValue (exclusive)</returns>
    public static int Next(int minValue, int maxValue)
    {
        if (maxValue <= minValue)
        {
            Throw.ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be larger then minValue");
        }

        // Rejection-sampled by the framework, so the distribution is uniform and the upper bound is
        // genuinely exclusive. It also draws from a shared generator rather than creating one per call.
        return RandomNumberGenerator.GetInt32(minValue, maxValue);
    }
}