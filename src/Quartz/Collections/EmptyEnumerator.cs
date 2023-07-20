using System.Collections;

namespace Quartz.Collections;

internal sealed class EmptyEnumerator<T> : IEnumerator<T>
{
    public static EmptyEnumerator<T> Instance = new EmptyEnumerator<T>();

    public bool MoveNext() => false;

    public void Reset()
    {
    }

    public T Current => default!;

    object? IEnumerator.Current => Current;

    public void Dispose()
    {
    }
}