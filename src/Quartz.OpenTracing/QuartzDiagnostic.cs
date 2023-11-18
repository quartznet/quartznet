using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTracing;
using OpenTracing.Tag;
using Quartz.Logging;
using static Quartz.Logging.OperationName;

namespace Quartz.OpenTracing;

internal sealed class QuartzDiagnostic : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private ILogger<QuartzDiagnostic> logger { get; }
    private ITracer tracer { get; }
    private readonly QuartzDiagnosticOptions options;

    private readonly List<IDisposable> _subscriptions = new List<IDisposable>();

    private readonly string jobExecuteStartEventName;
    private readonly string jobExecuteStopEventName;
    private readonly string jobExecuteExceptionEventName;

    public QuartzDiagnostic(ILogger<QuartzDiagnostic> logger,
        ITracer tracer,
        IOptions<QuartzDiagnosticOptions> options)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        jobExecuteStartEventName = Job.Execute + ".Start";
        jobExecuteStopEventName = Job.Execute + ".Stop";
        jobExecuteExceptionEventName = Job.Execute + ".Exception";
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener diagnosticListener)
    {
        if (diagnosticListener.Name == DiagnosticHeaders.DefaultListenerName)
        {
            var subscription = diagnosticListener.Subscribe(this, IsEnabled);
            _subscriptions.Add(subscription);
        }
    }

    void IObserver<DiagnosticListener>.OnError(Exception error)
    { }

    void IObserver<DiagnosticListener>.OnCompleted()
    {
        _subscriptions.ForEach(x => x.Dispose());
        _subscriptions.Clear();
    }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            OnNext(value.Key, value.Value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Event-Exception: {Event}", value.Key);
        }
    }

    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error)
    { }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted()
    { }

    private bool IsEnabled(string eventName) => eventName == Job.Execute;

    private void OnNext(string eventName, object? untypedArg)
    {
        if (eventName == jobExecuteStartEventName)
        {
            if (untypedArg is not IJobExecutionContext jobContext)
                throw new ArgumentException("Invalid context", nameof(untypedArg));

            if (IgnoreEvent(jobContext))
            {
                logger.LogDebug("Ignoring job due to IgnorePatterns");
                return;
            }

            var operationName = options.OperationNameResolver(jobContext);

            tracer.BuildSpan(operationName)
                .WithTag(Tags.SpanKind, Tags.SpanKindServer)
                .WithTag(Tags.Component, options.ComponentName)
                .WithTag(DiagnosticHeaders.SchedulerName, jobContext.Scheduler.SchedulerName)
                .WithTag(DiagnosticHeaders.SchedulerId, jobContext.Scheduler.SchedulerInstanceId)
                .WithTag(DiagnosticHeaders.FireInstanceId, jobContext.FireInstanceId)
                .WithTag(DiagnosticHeaders.TriggerGroup, jobContext.Trigger.Key.Group)
                .WithTag(DiagnosticHeaders.TriggerName, jobContext.Trigger.Key.Name)
                .WithTag(DiagnosticHeaders.JobType, jobContext.JobDetail.JobType.ToString())
                .WithTag(DiagnosticHeaders.JobGroup, jobContext.JobDetail.Key.Group)
                .WithTag(DiagnosticHeaders.JobName, jobContext.JobDetail.Key.Name)
                .StartActive();
        }
        else if (eventName == jobExecuteExceptionEventName)
        {
            if (untypedArg is not JobExecutionException jobException)
                throw new ArgumentException("invalid context", nameof(untypedArg));

            var scope = tracer.ScopeManager.Active;
            if (scope == null)
                return;

            SetSpanException(scope.Span, jobException);

            scope.Dispose();
        }
        else if (eventName == jobExecuteStopEventName)
        {
            tracer.ScopeManager.Active?.Dispose();
        }
    }

    private bool IgnoreEvent(IJobExecutionContext context)
        => options.IgnorePatterns.Any(ignore => ignore(context));

    private void SetSpanException(ISpan span, JobExecutionException exception)
    {
        span.SetTag(Tags.Error, true);

        span.Log(new Dictionary<string, object>(3)
        {
            { LogFields.Event, Tags.Error.Key },
            { LogFields.ErrorKind, exception.GetType().Name },
            { LogFields.ErrorObject, options.IncludeExceptionDetails
                ? exception
                : $"{nameof(QuartzDiagnosticOptions.IncludeExceptionDetails)} is disabled" }
        });
    }
}