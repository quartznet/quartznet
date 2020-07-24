namespace Quartz.OpenTelemetry.Instrumentation
{
    public class QuartzInstrumentationOptions
    {
        /// <summary>
        /// Whether to add exception details to logs. Defaults to false as they may contain
        /// Personally Identifiable Information (PII), passwords or usernames.
        /// </summary>
        public bool IncludeExceptionDetails { get; set; }
    }
}