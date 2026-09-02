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

namespace Quartz.Tests.Integration.Seeder;

/// <summary>
/// The one job type every seeded row names, so that a 4.0 process needs one type alias rather than
/// several to run what 3.20 stored.
/// </summary>
/// <remarks>
/// <para>
/// Its name goes into <c>JOB_CLASS_NAME</c> and names an assembly the rehearsal does not have, which
/// is the situation every upgrading application whose job types were renamed is in. The manifest
/// carries the stored spelling verbatim so the rehearsal can map it with
/// <c>UseTypeLoader(o =&gt; o.Map(…))</c> — the mechanism the migration guide teaches for exactly this,
/// exercised here against a name a released 3.20 really wrote.
/// </para>
/// <para>
/// It blocks when its data map says to. That is how the seeder gets a <c>QRTZ_FIRED_TRIGGERS</c> row
/// it can abandon: one firing is left mid-execution and the process is killed under it, which is what
/// a crashed 3.x node leaves behind.
/// </para>
/// </remarks>
public sealed class LegacyWorkerJob : IJob
{
    /// <summary>The data map key that turns one firing into a firing that never ends.</summary>
    public const string BlockKey = "blockForever";

    private static readonly TaskCompletionSource<bool> Executing = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once a blocking firing has actually reached the job.</summary>
    public static Task Blocked => Executing.Task;

    public async Task Execute(IJobExecutionContext context)
    {
        if (!context.MergedJobDataMap.ContainsKey(BlockKey))
        {
            return;
        }

        Executing.TrySetResult(true);

        // Never completes. The process is killed while this firing is in flight, on purpose.
        await Task.Delay(Timeout.Infinite, context.CancellationToken).ConfigureAwait(false);
    }
}
