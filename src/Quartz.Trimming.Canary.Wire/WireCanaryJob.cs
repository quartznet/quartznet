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

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The job the client schedules. It reaches the host as a type name in a request body, and the host
/// builds it from its own container, with a logger, when the client triggers it.
/// </summary>
public sealed class WireCanaryJob : IJob
{
    /// <summary>
    /// What the job logs, which comes back over HTTP as the execution's captured log.
    /// </summary>
    internal const string LogLine = "The wire canary job ran";

    private readonly ILogger<WireCanaryJob> logger;

    public WireCanaryJob(ILogger<WireCanaryJob> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// What the job was handed: the job's own data and the data the client triggered it with. Signalled by
    /// the job itself, so nothing waits on a sleep.
    /// </summary>
    internal static TaskCompletionSource<WireCanaryRun> Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(LogLine);

        Ran.TrySetResult(new WireCanaryRun(
            context.MergedJobDataMap.GetString(WireCanaryRun.PayloadKey),
            context.MergedJobDataMap.GetString(WireCanaryRun.FiredByKey)));

        return default;
    }
}

/// <summary>
/// The two values <see cref="WireCanaryJob" /> reads: one written when the job was scheduled, one when it
/// was triggered, both over HTTP.
/// </summary>
internal sealed record WireCanaryRun(string? Payload, string? FiredBy)
{
    public const string PayloadKey = "payload";
    public const string FiredByKey = "fired-by";
}
