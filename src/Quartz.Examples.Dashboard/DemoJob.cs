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

namespace Quartz.Examples.Dashboard;

[DisallowConcurrentExecution]
public sealed class DemoJob(ILogger<DemoJob> logger, TimeProvider timeProvider) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        int durationSeconds = context.MergedJobDataMap.GetInt("DurationSeconds");

        logger.LogInformation("Demo job {JobKey} started; duration: {DurationSeconds}s", context.JobDetail.Key, durationSeconds);

        await Task.Delay(TimeSpan.FromSeconds(durationSeconds), timeProvider, cancellationToken).ConfigureAwait(false);

        if (context.MergedJobDataMap.GetBoolean("Fail"))
        {
            throw new JobExecutionException("Intentional demo failure: this job shows failed executions on the timeline.");
        }

        logger.LogInformation("Demo job {JobKey} completed", context.JobDetail.Key);
    }
}
