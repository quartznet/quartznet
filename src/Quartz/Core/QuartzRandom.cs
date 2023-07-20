using System.Security.Cryptography;

namespace Quartz.Core;

public class QuartzRandom
{
#pragma warning disable SYSLIB0023
    private readonly RNGCryptoServiceProvider random;
#pragma warning restore SYSLIB0023

    internal QuartzRandom()
    {
#pragma warning disable SYSLIB0023
        random = new RNGCryptoServiceProvider();
#pragma warning restore SYSLIB0023
    }

    private double NextDouble()
    {
        byte[] buf = new byte[4];
        random.GetBytes(buf);
        return (double) BitConverter.ToUInt32(buf, 0) / uint.MaxValue;
    }

    /// <summary>
    /// Random number generator
    /// </summary>
    /// <param name="maxValue"></param>
    /// <returns>int between 0 and maxValue</returns>
    public int Next(int maxValue)
    {
        return Next(0, maxValue);

    }
    /// <summary>
    /// Random number generator
    /// </summary>
    /// <returns>a positive integer</returns>
    public int Next()
    {
        return Next(0, int.MaxValue);
    }
    /// <summary>
    /// Random number generator
    /// </summary>
    /// <param name="minValue"></param>
    /// <param name="maxValue"></param>
    /// <returns>integer between minValue and maxValue</returns>
    public int Next(int minValue, int maxValue)
    {
        if (maxValue <= minValue)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(maxValue), "maxValue must be larger then minValue");
        }

        long range = (long)maxValue - minValue;
        return (int) Math.Floor(NextDouble() * range) + minValue;
    }
}