using System.Collections;

namespace Quartz.Collections;

internal sealed class EmptyReadOnlyCollection<T> : IReadOnlyCollection<T>
{
    public static readonly IReadOnlyCollection<T> Instance = new EmptyReadOnlyCollection<T>();

#pragma warning disable CA1822
    public EmptyEnumerator<T> GetEnumerator() => EmptyEnumerator<T>.Instance;
#pragma warning restore CA1822

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => EmptyEnumerator<T>.Instance;

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => 0;
}