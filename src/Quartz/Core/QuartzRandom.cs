using System.Security.Cryptography;

namespace Quartz.Core;

internal static class QuartzRandom
{
    /// <summary>
    /// Random number generator
    /// </summary>
    /// <param name="maxValue"></param>
    /// <returns>int between 0 (inclusive) and maxValue (exclusive)</returns>
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