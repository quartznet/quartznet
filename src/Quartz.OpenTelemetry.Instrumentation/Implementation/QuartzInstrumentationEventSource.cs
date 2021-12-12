using System.Diagnostics.Tracing;

namespace Quartz.OpenTelemetry.Instrumentation.Implementation
{
    /// <summary>
    /// EventSource events emitted from the project.
    /// </summary>
    [EventSource(Name = "OpenTelemetry-Instrumentation-Quartz")]
    internal sealed class QuartzInstrumentationEventSource : EventSource
    {
        public static readonly QuartzInstrumentationEventSource Log = new QuartzInstrumentationEventSource();

        [Event(1, Message = "Payload is NULL in event '{1}' from handler '{0}', span will not be recorded.", Level = EventLevel.Warning)]
        public void NullPayload(string handlerName, string eventName)
        {
            WriteEvent(1, handlerName, eventName);
        }

        [Event(2, Message = "Request is filtered out.", Level = EventLevel.Verbose)]
        public void RequestIsFilteredOut(string eventName)
        {
            WriteEvent(2, eventName);
        }
    }
}