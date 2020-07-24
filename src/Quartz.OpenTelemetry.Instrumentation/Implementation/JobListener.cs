using System;
using System.Diagnostics;

using OpenTelemetry.Instrumentation;
using OpenTelemetry.Trace;

namespace Quartz.OpenTelemetry.Instrumentation.Implementation
{
    internal sealed class JobListener : ListenerHandler
    {
        private readonly QuartzInstrumentationOptions options;
        private readonly ActivitySourceAdapter activitySource;

        public JobListener(string sourceName, QuartzInstrumentationOptions options, ActivitySourceAdapter activitySource) 
            : base(sourceName)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.activitySource = activitySource;
        }

        public override void OnStartActivity(Activity activity, object payload)
        {
            if (!(payload is IJobExecutionContext jobExecutionContext))
            {
                QuartzInstrumentationEventSource.Log.NullPayload(nameof(JobListener), nameof(OnStartActivity));
                return;
            }

            activitySource.Start(activity);

            if (activity.IsAllDataRequested)
            {
                //SetActivityAttributes(activity, jobExecutionContext, exception: null);
            }
        }

        public override void OnStopActivity(Activity activity, object payload)
        {
            if (!(payload is IJobExecutionContext))
            {
                QuartzInstrumentationEventSource.Log.NullPayload(nameof(JobListener), nameof(OnStopActivity));
                return;
            }

            activitySource.Stop(activity);
        }

        public override void OnException(Activity activity, object payload)
        {
            if (!(payload is JobExecutionException exception))
            {
                QuartzInstrumentationEventSource.Log.NullPayload(nameof(JobListener), nameof(OnStopActivity));
                return;
            }

            activity.AddTag("error", "true");
            activity.AddTag("error.message", options.IncludeExceptionDetails ? exception.Message : $"{nameof(QuartzInstrumentationOptions.IncludeExceptionDetails)} is disabled");
            activity.AddTag("error.stacktrace", options.IncludeExceptionDetails ? exception.StackTrace : $"{nameof(QuartzInstrumentationOptions.IncludeExceptionDetails)} is disabled");
        }
    }
}