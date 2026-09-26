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

using Microsoft.Extensions.Logging;

namespace Quartz.Diagnostics;

/// <summary>
/// The logger provider <c>UseExecutionLogCapture()</c> adds to the container, which writes a line into
/// the buffer of the firing it was logged on and ignores every other line.
/// </summary>
/// <remarks>
/// <para>
/// One per container, whichever schedulers in it capture: a line is kept only when the flow it was
/// logged on belongs to a firing whose scheduler began a buffer for it, so the provider needs to know
/// nothing about which schedulers those are. Outside a firing a log call costs it one async-local read.
/// </para>
/// <para>
/// Filtered like every other provider — <c>Logging:LogLevel</c>, or <c>Logging:QuartzExecutionLog</c>
/// for this one alone — so a job's <c>Debug</c> lines are kept only where the configuration asks for
/// them.
/// </para>
/// </remarks>
[ProviderAlias("QuartzExecutionLog")]
internal sealed class ExecutionLogCaptureProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new ExecutionLogCaptureLogger(categoryName);
    }

    public void Dispose()
    {
        // Holds nothing: every buffer belongs to a firing, and goes with its context.
    }
}

/// <summary>
/// One category's logger, writing into whichever firing's buffer the call is made on.
/// </summary>
internal sealed class ExecutionLogCaptureLogger : ILogger
{
    private readonly string category;

    public ExecutionLogCaptureLogger(string category)
    {
        this.category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    /// <summary>
    /// Enabled only inside a firing that is being captured, so a message nothing will keep is never
    /// formatted for this provider.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && ExecutionLogCapture.Current is not null;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.None)
        {
            return;
        }

        ExecutionLogBuffer? buffer = ExecutionLogCapture.Current;
        if (buffer is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        buffer.Append(logLevel, category, formatter(state, exception), exception);
    }
}
