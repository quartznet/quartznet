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

namespace Quartz.Examples.Example10;

/// <summary>
/// This is just a simple job that gets fired off many times by example 10.
/// </summary>
/// <author>Bill Kratzer</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleJob : IJob
{
    // job parameter
    public const string DelayMilliseconds = "delay milliseconds";

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a
    /// <see cref="ITrigger" /> fires that is associated with
    /// the <see cref="IJob" />.
    /// </summary>
    public virtual async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        JobKey jobKey = context.JobDetail.Key;

        Console.WriteLine($"Executing {jobKey} at {context.FireTimeUtc.LocalDateTime:HH:mm:ss.fff}");

        // pretend to do some work, for as long as this job was told to
        int delay = context.JobDetail.JobDataMap.GetInt(DelayMilliseconds);
        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);

        Console.WriteLine($"Finished {jobKey} after {delay} ms");
    }
}
