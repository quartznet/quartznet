using System.Collections.Generic;

using Quartz.Logging;

namespace Quartz.OpenTelemetry.Instrumentation;

public class QuartzInstrumentationOptions
{
    /// <summary>
    /// Whether to add exception details to logs. Defaults to false as they may contain
    /// Personally Identifiable Information (PII), passwords or usernames.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }
        
    /// <summary>
    /// Default traced operations.
    /// </summary>
    public static readonly IEnumerable<string> DefaultTracedOperations = new[]
    {
        OperationName.Job.Execute,
        OperationName.Job.Veto
    };

    /// <summary>
    /// Gets or sets traced operations set.
    /// </summary>
    public HashSet<string> TracedOperations { get; set; } = new HashSet<string>(DefaultTracedOperations);
}