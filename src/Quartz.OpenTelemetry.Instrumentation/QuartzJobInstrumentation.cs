using System;

using OpenTelemetry.Instrumentation;
using OpenTelemetry.Trace;

using Quartz.Logging;
using Quartz.OpenTelemetry.Instrumentation.Implementation;

namespace Quartz.OpenTelemetry.Instrumentation
{
    internal sealed class QuartzJobInstrumentation : IDisposable
    {
        private readonly DiagnosticSourceSubscriber diagnosticSourceSubscriber;

        public QuartzJobInstrumentation(ActivitySourceAdapter activitySource)
            : this(activitySource, new QuartzInstrumentationOptions())
        {
        }

        public QuartzJobInstrumentation(ActivitySourceAdapter activitySource, QuartzInstrumentationOptions options)
        {
            var listener = new QuartzDiagnosticListener(DiagnosticHeaders.DefaultListenerName, options, activitySource);
            diagnosticSourceSubscriber = new DiagnosticSourceSubscriber(listener, null);
            diagnosticSourceSubscriber.Subscribe();
        }

        public void Dispose()
        {
            diagnosticSourceSubscriber?.Dispose();
        }
    }
}