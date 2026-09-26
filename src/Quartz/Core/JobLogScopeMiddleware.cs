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

using Quartz.Diagnostics;

namespace Quartz.Core;

/// <summary>
/// Opens a <see cref="JobLogScope" /> around each firing, so every line logged inside it names the job,
/// the trigger and the fire instance.
/// </summary>
/// <remarks>
/// <para>
/// This is the per-firing scope <see cref="JobRunShell" /> does not open on its own: a scope is an
/// <c>AsyncLocal</c> write, and one per firing was measured at 240 bytes and about 130 ns of a
/// no-op firing's 960. As a middleware it costs nothing until <c>AddJobLogScope</c> asks for it.
/// </para>
/// <para>
/// The logger comes from the scheduler's own <see cref="ILoggerFactory" />, the container's, rather than
/// from <see cref="LogProvider" />: a scope is pushed onto the factory that opens it, so it reaches the
/// job's own lines only when the job logs through the same one.
/// </para>
/// </remarks>
internal sealed class JobLogScopeMiddleware : IJobExecutionMiddleware
{
    private readonly ILogger logger;

    public JobLogScopeMiddleware(ILoggerFactory loggerFactory)
    {
        logger = loggerFactory.CreateLogger<JobLogScopeMiddleware>();
    }

    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        // An async method, so the scope leaves with it: the caller's execution context is restored when
        // this returns, and nothing the run shell logs afterwards carries a scope already disposed.
        using IDisposable? scope = logger.BeginScope(new JobLogScope(context));
        await next(context, cancellationToken).ConfigureAwait(false);
    }
}
