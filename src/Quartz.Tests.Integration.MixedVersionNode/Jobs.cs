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

using System.Diagnostics;

namespace Quartz.Tests.Integration.MixedVersionNode;

// Public with public constructors: the store hands the job factory nothing but the type it read back
// out of JOB_CLASS_NAME, and both builds of this assembly spell that type the same way.

/// <summary>
/// Records one execution when it has run, after whatever the job does in <see cref="Run" />.
/// </summary>
/// <remarks>
/// On the working tree it also reports the run, which a node keeping its history writes into the 4.4
/// outcome columns: the result, a summary naming the node and the firing, and one metric.
/// </remarks>
public abstract class RecordingJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        long startedUtc = TimeProvider.System.GetUtcNow().UtcTicks;
        long started = Stopwatch.GetTimestamp();

        FireProgress? progress = await Run(context, cancellationToken).ConfigureAwait(false);

        long ended = Stopwatch.GetTimestamp();
        await Runs.Record(context, startedUtc, started, ended, progress).ConfigureAwait(false);

#if QUARTZ_WORKING_TREE
        context.Result = JobRunReport.Succeeded($"{context.Scheduler.SchedulerInstanceId} ran {context.FireInstanceId}")
            .With("node", context.Scheduler.SchedulerInstanceId);
#endif
    }

    /// <returns>What the firing's own row said its progress was, for a job that reports one.</returns>
    private protected virtual ValueTask<FireProgress?> Run(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<FireProgress?>(null);
    }
}

/// <summary>The one-offs: concurrent, and done as soon as they are recorded.</summary>
public sealed class OneOffJob : RecordingJob;

/// <summary>Parents and their continuations.</summary>
public sealed class ChainJob : RecordingJob;

/// <summary>The triggers that carry a pause reason or an overlap policy.</summary>
public sealed class StateJob : RecordingJob;

/// <summary>
/// A job that must never run beside itself, held for long enough that an overlap anywhere in the
/// cluster lands inside the window.
/// </summary>
[DisallowConcurrentExecution]
public sealed class SerialJob : RecordingJob
{
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(100);

    private protected override async ValueTask<FireProgress?> Run(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(Hold, cancellationToken).ConfigureAwait(false);
        return null;
    }
}

/// <summary>
/// On the working tree, reports progress, waits for it to reach its fired-trigger row, holds while the
/// other node writes rows of its own, and reads the row again. On the released build it only holds, so
/// that its fired rows sit beside the working tree's.
/// </summary>
public sealed class ProgressJob : RecordingJob
{
    public const int ReportedPercent = 57;

    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);

    public static string ReportedMessage(string fireInstanceId) => "reported by " + fireInstanceId;

    private protected override async ValueTask<FireProgress?> Run(IJobExecutionContext context, CancellationToken cancellationToken)
    {
#if QUARTZ_WORKING_TREE
        context.ReportProgress(ReportedPercent, ReportedMessage(context.FireInstanceId));

        // The first report is written at once, but off this thread, so the row is watched until it
        // says so rather than assumed to.
        Stopwatch waited = Stopwatch.StartNew();
        while ((await Runs.ReadProgress(context.FireInstanceId).ConfigureAwait(false)).Percent != ReportedPercent
               && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
#endif

        await Task.Delay(Hold, cancellationToken).ConfigureAwait(false);

#if QUARTZ_WORKING_TREE
        return await Runs.ReadProgress(context.FireInstanceId).ConfigureAwait(false);
#else
        return null;
#endif
    }
}
