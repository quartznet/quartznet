using System.Collections;

namespace Quartz.Collections;

internal sealed class EmptyReadOnlyCollection<T> : IReadOnlyCollection<T>
{
    public static readonly IReadOnlyCollection<T> Instance = new EmptyReadOnlyCollection<T>();

    public EmptyEnumerator<T> GetEnumerator() => EmptyEnumerator<T>.Instance;

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => EmptyEnumerator<T>.Instance;

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => 0;
}