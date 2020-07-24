using System;
using System.Diagnostics;

using OpenTelemetry.Instrumentation;
using OpenTelemetry.Trace;

namespace Quartz.OpenTelemetry.Instrumentation.Implementation
{
    internal sealed class QuartzDiagnosticListener : ListenerHandler
    {
        private readonly QuartzInstrumentationOptions options;
        private readonly ActivitySourceAdapter activitySource;

        public QuartzDiagnosticListener(string sourceName, QuartzInstrumentationOptions options, ActivitySourceAdapter activitySource) 
            : base(sourceName)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.activitySource = activitySource;
        }

        public override void OnStartActivity(Activity activity, object payload)
        {
            if (!options.TracedOperations.Contains(activity.OperationName))
            {
                return;
            }
            activitySource.Start(activity);
        }

        public override void OnStopActivity(Activity activity, object payload)
        {
            if (!options.TracedOperations.Contains(activity.OperationName))
            {
                return;
            }
            activitySource.Stop(activity);
        }

        public override void OnException(Activity activity, object payload)
        {
            if (!options.TracedOperations.Contains(activity.OperationName))
            {
                return;
            }
            
            if (!(payload is Exception exception))
            {
                QuartzInstrumentationEventSource.Log.NullPayload(nameof(QuartzDiagnosticListener), nameof(OnStopActivity));
                return;
            }

            activity.AddTag("error", "true");
            activity.AddTag("error.message", options.IncludeExceptionDetails ? exception.Message : $"{nameof(QuartzInstrumentationOptions.IncludeExceptionDetails)} is disabled");
            activity.AddTag("error.stacktrace", options.IncludeExceptionDetails ? exception.StackTrace : $"{nameof(QuartzInstrumentationOptions.IncludeExceptionDetails)} is disabled");
        }
    }
}