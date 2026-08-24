using System.Diagnostics.Metrics;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// The jobs and listeners the tutorial samples name in passing.
/// </summary>
/// <remarks>
/// A page that shows one of these in full wraps it in its own region; the rest are here only so the
/// samples that name them compile.
/// </remarks>
public sealed class MyJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class BackupJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class AuditListener : IJobListener;

public sealed class ReportAuditListener : IJobListener;

public sealed class NightlyListener : ITriggerListener;

public sealed class VetoWeekends : ITriggerListener;

public sealed class MeteredListener(IMeterFactory meterFactory) : IJobListener;

public interface IReportRenderer;

public sealed class PdfReportRenderer : IReportRenderer;
