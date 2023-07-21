namespace Quartz;

/// <summary>
/// A time source for Quartz.NET that returns the current time.
/// Original idea by Ayende Rahien:
/// http://ayende.com/Blog/archive/2008/07/07/Dealing-with-time-in-tests.aspx
/// </summary>
public static class SystemTime
{
    private static ThreadLocal<bool> cacheIsInUse = new();
    private static ThreadLocal<DateTimeOffset?> cachedNow = new();
    private static ThreadLocal<DateTimeOffset?> cachedUtcNow = new();

    /// <summary>
    /// Return current UTC time via <see cref="Func{TResult}" />. Allows easier unit testing.
    /// </summary>
    public static Func<DateTimeOffset> UtcNow = () => cacheIsInUse.Value
        ? cachedUtcNow.Value ?? (cachedUtcNow.Value = DateTimeOffset.UtcNow).Value
        : DateTimeOffset.UtcNow;

    /// <summary>
    /// Return current time in current time zone via <see cref="Func&lt;T&gt;" />. Allows easier unit testing.
    /// </summary>
    public static Func<DateTimeOffset> Now = () => cacheIsInUse.Value
        ? cachedNow.Value ?? (cachedNow.Value = DateTimeOffset.Now).Value
        : DateTimeOffset.Now;

    public static void InitCache()
    {
        cacheIsInUse.Value = true;
    }

    public static void ClearCache()
    {
        cacheIsInUse.Value = false;
        cachedNow.Value = null;
        cachedUtcNow.Value = null;
    }
}