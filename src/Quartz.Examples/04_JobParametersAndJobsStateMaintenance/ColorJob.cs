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

namespace Quartz.Examples.Example04;

/// <summary>
/// This is just a simple job that receives parameters and
/// maintains state.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
[PersistJobDataAfterExecution]
[DisallowConcurrentExecution]
public class ColorJob : IJob
{
    // parameter names specific to this job
    public const string FavoriteColor = "favorite color";
    public const string ExecutionCount = "count";

    // Since Quartz builds a fresh instance of the job for every firing, a field cannot carry state
    // from one firing to the next. Watch this one stay at 1 while the map's count climbs.
    private int counter = 1;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a
    /// <see cref="ITrigger" /> fires that is associated with
    /// the <see cref="IJob" />.
    /// </summary>
    public virtual ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobKey jobKey = context.JobDetail.Key;

        // Grab and print passed parameters
        JobDataMap data = context.JobDetail.JobDataMap;
        string? favoriteColor = data.GetString(FavoriteColor);
        int count = data.GetInt(ExecutionCount);

        Console.WriteLine(
            $"ColorJob: {jobKey} fired at {context.FireTimeUtc.LocalDateTime:HH:mm:ss}, favorite color {favoriteColor}, "
            + $"count from the map {count}, count from a field {counter}");

        // increment the count and store it back into the job map. [PersistJobDataAfterExecution] is
        // what makes the store keep it, so the next firing reads what this one wrote.
        count++;
        data[ExecutionCount] = count;

        // and the field, which starts again from 1 every time
        counter++;

        return default;
    }
}
