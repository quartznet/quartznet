using System;
using System.Collections.Generic;

namespace Quartz.Util
{
    internal static class SortedSetExtensions
    {
        internal static SortedSet<int> TailSet(this SortedSet<int> set, int value)
        {
            return set.GetViewBetween(value, 9999999);
        }

        /// <summary>
        /// Returns the first element of the specified <see cref="SortedSet{TSource}"/>, or a default
        /// value if no element is found.
        /// </summary>
        /// <typeparam name="TSource">The type of the elements of <paramref name="source"/>.</typeparam>
        /// <param name="source">The <see cref="SortedSet{TSource}"/> to return the first element of.</param>
        /// <returns>
        /// The default value for <typeparamref name="TSource"/> if <paramref name="source"/> is empty;
        /// otherwise, the first element in <paramref name="source"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        internal static TSource? FirstOrDefault<TSource>(this SortedSet<TSource> source) where TSource : class
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            using var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return null;
            }

            return enumerator.Current;
        }
    }
}