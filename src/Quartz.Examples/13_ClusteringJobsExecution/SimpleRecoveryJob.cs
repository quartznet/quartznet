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

namespace Quartz.Examples.Example13;

/// <summary>
/// A job that takes long enough to be caught mid-execution when its node is killed, and says which
/// node is running it.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleRecoveryJob : IJob
{
    private const string Count = "count";

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a
    /// <see cref="ITrigger" /> fires that is associated with
    /// the <see cref="IJob" />.
    /// </summary>
    public virtual async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobKey jobKey = context.JobDetail.Key;
        string node = context.Scheduler.SchedulerInstanceId;

        // Recovering means this firing is a second attempt: the node that was running it died, and
        // this node picked the work up because the job asked for RequestRecovery()
        string what = context.Recovering ? "RECOVERING" : "starting";
        Console.WriteLine($"SimpleRecoveryJob: {jobKey} {what} on {node} at {context.FireTimeUtc.LocalDateTime:HH:mm:ss}");

        // long enough that killing this process mid-run leaves work for another node to recover
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

        JobDataMap data = context.JobDetail.JobDataMap;
        int count = data.TryGetInt(Count, out int previous) ? previous + 1 : 1;
        data[Count] = count;

        Console.WriteLine($"SimpleRecoveryJob: {jobKey} done on {node}, execution #{count}");
    }
}
