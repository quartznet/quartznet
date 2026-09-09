using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quartz.Diagnostics;

/// <summary>
/// The <see cref="ILogger{T}" /> a Quartz component is injected in a container that was never told
/// where logging goes.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddQuartz</c> registers this for the open <see cref="ILogger{T}" />, and registers no
/// <see cref="ILoggerFactory" /> at all. That second half is the point, and it is what
/// <see cref="Microsoft.Extensions.Logging.Logger{T}" /> — which takes a factory the ordinary way and
/// so needs one registered — cannot do on its own: Quartz must not be the thing that answers "where do
/// log lines go", because every registration in this area is a <c>TryAdd</c> and the first one wins. A
/// factory registered by <c>AddQuartz</c> would beat the <c>services.AddLogging(b =&gt; b.AddConsole())</c>
/// an application writes on the line after it, and the application's providers would be dropped in
/// silence.
/// </para>
/// <para>
/// So the factory is read from the container when the logger is constructed, and only then: an
/// application that configured logging before or after <c>AddQuartz</c>, or through a host that
/// configured it before either, has an <see cref="ILoggerFactory" /> by the time anything is resolved,
/// and this hands out its loggers. One that configured none gets <see cref="LogProviderLoggerFactory" />,
/// which is where the rest of Quartz already sends a line it cannot inject a logger for.
/// </para>
/// <para>
/// It also loses, deliberately, wherever it can: <c>AddLogging</c> registers
/// <see cref="Microsoft.Extensions.Logging.Logger{T}" /> for the same open generic with its own
/// <c>TryAdd</c>, so in a container where logging was configured first — every generic host — that is
/// the type resolved and this one is never constructed. The two behave identically when a factory is
/// registered; this one only adds an answer for when there is none.
/// </para>
/// </remarks>
/// <typeparam name="T">The type the log category is named for.</typeparam>
internal sealed class QuartzLogger<T> : Logger<T>
{
    public QuartzLogger(IServiceProvider provider)
        : base(provider.GetService<ILoggerFactory>() ?? LogProviderLoggerFactory.Instance)
    {
    }
}
