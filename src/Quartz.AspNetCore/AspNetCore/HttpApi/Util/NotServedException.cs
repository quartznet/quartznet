namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// A read the server cannot answer because what serves it keeps no such thing: a per-job run status,
/// asked of a history store that keeps rows only.
/// </summary>
/// <remarks>
/// <see cref="ExceptionHandler" /> answers it <c>501</c> with problem details naming
/// <see cref="NotSupportedException" />, which the HTTP client raises again as that. It is not the
/// <c>404</c> without problem details a host older than the route answers, and not a server fault: the
/// store's own <see cref="NotSupportedException" /> is the message.
/// </remarks>
internal sealed class NotServedException : Exception
{
    public NotServedException(NotSupportedException innerException) : base(innerException?.Message, innerException)
    {
    }
}
