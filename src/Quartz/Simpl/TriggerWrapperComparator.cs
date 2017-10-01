using System;
using System.Collections.Generic;

namespace Quartz.Simpl
{
    /// <summary>
    /// Comparer for trigger wrappers.
    /// </summary>
    internal class TriggerWrapperComparator : IComparer<TriggerWrapper>, IEquatable<TriggerWrapperComparator>
    {
        private readonly TriggerTimeComparator ttc = new TriggerTimeComparator();

        public int Compare(TriggerWrapper trig1, TriggerWrapper trig2)
        {
            return ttc.Compare(trig1.Trigger, trig2.Trigger);
        }

        public override bool Equals(object obj)
        {
            return obj is TriggerWrapperComparator;
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other"/> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        public bool Equals(TriggerWrapperComparator other)
        {
            return true;
        }

        /// <summary>
        /// Serves as a hash function for a particular type. 
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"/>.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            return ttc?.GetHashCode() ?? 0;
        }
    }
}