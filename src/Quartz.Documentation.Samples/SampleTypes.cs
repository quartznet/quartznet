using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Jobs;

namespace Quartz.Documentation.Samples;

/// <summary>
/// The jobs, listeners and option classes the documentation samples refer to.
/// </summary>
/// <remarks>
/// A page that shows one of these in full wraps it in its own region; the rest are here only so the
/// samples that name them compile.
/// </remarks>
public sealed class ExampleJob : IJob
{
    public string InjectedString { get; set; } = "";

    public bool InjectedBool { get; set; }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class SlowJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public interface IReportStore;

public interface IEmailSender
{
    ValueTask SendDigest(CancellationToken cancellationToken = default);
}

public sealed class SendReportsJob : IJob
{
    public SendReportsJob(IReportStore store)
    {
    }

    public static SendReportsJob ForTenant(string tenant) => new(null!);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class SampleOptions
{
    public string CronSchedule { get; set; } = "";
}

public sealed class NotificationJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class ReportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class MainJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class CleanupJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class AuditSchedulerListener : ISchedulerListener;

public sealed class LoggingJobListener : IJobListener;

public sealed class MetricsTriggerListener : ITriggerListener;

public sealed class SampleSchedulerListener : ISchedulerListener;

public sealed class SampleJobListener : IJobListener;

public sealed class SampleTriggerListener : ITriggerListener;

public sealed class InboxListener : IDirectoryScanListener
{
    public ValueTask FilesUpdatedOrAdded(IReadOnlyCollection<FileInfo> updatedFiles, CancellationToken cancellationToken = default) => default;

    public ValueTask FilesDeleted(IReadOnlyCollection<FileInfo> deletedFiles, CancellationToken cancellationToken = default) => default;
}

public sealed class WarmUpBeforeSchedulingService(
    IHostApplicationLifetime applicationLifetime,
    IServiceProvider serviceProvider,
    IOptionsMonitor<QuartzHostedServiceOptions> options)
    : QuartzHostedService(applicationLifetime, serviceProvider, options);

/// <summary>
/// Stands in for the Blazor root component an application hosting the dashboard alongside its own
/// Razor components would have. Written in C# rather than as a .razor file so that the samples project
/// does not need the Razor SDK.
/// </summary>
public sealed class App : IComponent
{
    public void Attach(RenderHandle renderHandle)
    {
    }

    public Task SetParametersAsync(ParameterView parameters) => Task.CompletedTask;
}

public interface IMyPluginDependency;

public sealed class MyPluginDependency : IMyPluginDependency;

/// <summary>
/// Stands in for the holiday source a calendar would be built from — a database, a configuration
/// section, a service — which is what the generic <c>AddCalendar&lt;T&gt;</c> overloads cannot supply.
/// </summary>
public interface IHolidayList
{
    IReadOnlyList<DateOnly> Days { get; }
}

public sealed class MyPlugin : ISchedulerPlugin
{
    public MyPlugin()
    {
    }

    public MyPlugin(IMyPluginDependency dependency)
    {
    }

    public string? SomeSetting { get; set; }

    // Start and Shutdown are the interface's defaults, so a plugin that does its work in Initialize
    // declares one member rather than three.
    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default) => default;
}

