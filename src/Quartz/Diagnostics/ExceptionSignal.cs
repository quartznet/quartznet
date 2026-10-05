#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

namespace Quartz.Diagnostics;

/// <summary>
/// Whether a failed span records its exception as an <c>exception</c> span event, and the one place
/// <c>OTEL_SEMCONV_EXCEPTION_SIGNAL_OPT_IN</c> is read.
/// </summary>
/// <remarks>
/// <para>
/// OpenTelemetry deprecated span events in 2026, <c>RecordException</c> included, in favour of exceptions
/// emitted as log records correlated with the current span. Its semantic conventions name the variable an
/// instrumentation in a current major version honours: <c>logs</c> emits exceptions as logs only, and
/// <c>logs/dup</c> emits both. Anything else, unset included, keeps the span events. Quartz already logs
/// every exception a span records, through <c>ILogger</c> and while the span is current, so <c>logs</c>
/// only has to stop the span events and <c>logs/dup</c> is what Quartz has always done.
/// </para>
/// <para>
/// Read once per scheduler, when it is built: like every <c>OTEL_*</c> variable it is process
/// configuration, and a firing should not pay for an environment lookup. The typed option,
/// <see cref="QuartzSchedulerOptions.RecordExceptionSpanEvents" />, wins whenever it is set.
/// </para>
/// </remarks>
internal static class ExceptionSignal
{
    /// <summary>
    /// The environment variable OpenTelemetry's semantic conventions define for the opt-in.
    /// </summary>
    internal const string OptInVariable = "OTEL_SEMCONV_EXCEPTION_SIGNAL_OPT_IN";

    /// <summary>
    /// The value that moves exceptions to logs only. OpenTelemetry reads an enumerated environment value
    /// without regard to case.
    /// </summary>
    private const string LogsOnly = "logs";

    /// <summary>
    /// Whether a scheduler's failed spans record exception events: what the application set, or what the
    /// environment says when it set nothing.
    /// </summary>
    /// <param name="configured">
    /// <see cref="QuartzSchedulerOptions.RecordExceptionSpanEvents" />, or <see langword="null" /> when the
    /// application left it unset.
    /// </param>
    internal static bool RecordsSpanEvents(bool? configured)
    {
        return configured ?? RecordsSpanEventsUnder(Environment.GetEnvironmentVariable(OptInVariable));
    }

    /// <summary>
    /// Whether an opt-in value keeps the span events: every value but <c>logs</c> does.
    /// </summary>
    /// <param name="optIn">The value of <see cref="OptInVariable" />, or <see langword="null" /> when it is unset.</param>
    internal static bool RecordsSpanEventsUnder(string? optIn)
    {
        return !string.Equals(optIn?.Trim(), LogsOnly, StringComparison.OrdinalIgnoreCase);
    }
}
