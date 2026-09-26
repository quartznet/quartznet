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

using Quartz.Diagnostics;

namespace Quartz.Core;

/// <summary>
/// Begins the capture buffer of every firing its scheduler executes, which is what makes that
/// scheduler's firings the ones <see cref="ExecutionLogCaptureProvider" /> writes into.
/// </summary>
/// <remarks>
/// A middleware because enabling capture is a choice one scheduler makes: a container's logger provider
/// sees every scheduler's lines, and only the schedulers that registered this begin a buffer for them.
/// It begins the buffer and hands on; the buffer outlives the call, so what the run shell and the
/// listeners log afterwards, still inside the firing, is kept as well — the job's unhandled exception
/// among it.
/// </remarks>
internal sealed class ExecutionLogCaptureMiddleware : IJobExecutionMiddleware
{
    private readonly ExecutionLogCaptureOptions options;
    private readonly TimeProvider timeProvider;

    public ExecutionLogCaptureMiddleware(ExecutionLogCaptureOptions options, TimeProvider timeProvider)
    {
        this.options = options;
        this.timeProvider = timeProvider;
    }

    public ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        ExecutionLogCapture.Begin(context, options, timeProvider);
        return next(context, cancellationToken);
    }
}
