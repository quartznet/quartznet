using Quartz.Extensibility;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// The services, jobs and exceptions the how-to samples name in passing.
/// </summary>
/// <remarks>
/// A page that shows one of these in full wraps it in its own region; the rest are here only so the
/// samples that name them compile.
/// </remarks>
public interface IOrderService
{
    ValueTask<int> Process(string? region, CancellationToken cancellationToken = default);
}

public interface IImportService
{
    ValueTask Run(CancellationToken cancellationToken = default);
}

public sealed class TransientImportException : Exception
{
    public TransientImportException()
    {
    }

    public TransientImportException(string message) : base(message)
    {
    }

    public TransientImportException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class AnExampleJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class ReportingJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class AcmeOutboxOptions
{
    public TimeSpan DrainInterval { get; set; } = TimeSpan.FromSeconds(30);
}

public sealed class AcmeOutboxPlugin : ISchedulerPlugin
{
    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default) => default;
}
