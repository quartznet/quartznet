using System;
using System.Security.Cryptography;

namespace Quartz.Core
{
    public class QuartzRandom
    {
        private readonly RNGCryptoServiceProvider random;
        internal QuartzRandom()
        {
            random = new RNGCryptoServiceProvider();
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
            if(maxValue <= minValue)
                throw new ArgumentOutOfRangeException("maxValue", "maxValue must be larger then minValue");
            long range = maxValue - minValue;
            return (int) Math.Floor(NextDouble() * range) + minValue;
        }
    }
}
