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

namespace Quartz.Tests.Integration.MixedVersionNode;

/// <summary>
/// Quartz's log, at or above one level, to standard error, where the test keeps it for a failure message.
/// </summary>
/// <remarks>
/// Written here rather than taken from <c>Microsoft.Extensions.Logging.Console</c> because both builds
/// need it and the released one should carry no package the released Quartz does not already bring.
/// </remarks>
internal sealed class StandardErrorLoggerFactory(string node, LogLevel minimum) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new Logger(node, categoryName, minimum);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Logger(string node, string category, LogLevel minimum) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"[{node} {logLevel} {eventId.Id}] {category}: {formatter(state, exception)}";
            Console.Error.WriteLine(exception is null ? line : line + Environment.NewLine + exception);
        }
    }
}
