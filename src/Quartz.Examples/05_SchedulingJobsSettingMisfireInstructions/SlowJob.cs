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

namespace Quartz.Examples.Example05;

/// <summary>
/// A job that takes far longer to run than the interval its trigger asks for, which is what makes
/// its trigger misfire.
/// </summary>
/// <remarks>
/// <see cref="DisallowConcurrentExecutionAttribute" /> is what turns "slow" into "late": a second
/// firing cannot start beside the first, so it waits, and while it waits it falls behind.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[PersistJobDataAfterExecution]
[DisallowConcurrentExecution]
public class SlowJob : IJob
{
    public const string NumExecutions = "NumExecutions";
    public const string ExecutionDelaySeconds = "ExecutionDelaySeconds";

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// fires that is associated with the <see cref="IJob" />.
    /// </summary>
    public virtual async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobDataMap map = context.JobDetail.JobDataMap;

        int executeCount = map.TryGetInt(NumExecutions, out int previous) ? previous + 1 : 1;
        map[NumExecutions] = executeCount;

        int delaySeconds = map.TryGetInt(ExecutionDelaySeconds, out int configured) ? configured : 10;

        // ScheduledFireTimeUtc is when the trigger wanted this firing; FireTimeUtc is when it got it.
        // The gap between them is how late the trigger is running, and it grows every cycle.
        Console.WriteLine(
            $"---{context.JobDetail.Key} run #{executeCount} due at {context.ScheduledFireTimeUtc?.LocalDateTime:HH:mm:ss}, "
            + $"started at {context.FireTimeUtc.LocalDateTime:HH:mm:ss}, will take {delaySeconds} seconds");

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

        Console.WriteLine($"  -{context.JobDetail.Key} run #{executeCount} complete");
    }
}
