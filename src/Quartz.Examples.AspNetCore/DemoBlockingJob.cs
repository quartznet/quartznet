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

namespace Quartz.Examples.AspNetCore;

/// <summary>
/// A job that runs for 20 seconds and may not run beside itself, with two triggers that each want it every few
/// seconds, so one of them is nearly always held: what the dashboard's "Why isn't it firing?" shows, with the
/// firing that holds the job and <em>Interrupt</em>.
/// </summary>
/// <remarks>
/// <para>
/// While one trigger's firing runs, the other trigger is <see cref="TriggerState.Blocked" />. The running
/// trigger itself reads <see cref="TriggerState.Executing" />, which outranks Blocked, and its own next fire
/// time passes while it waits: the dashboard says it is held by its job's running firing.
/// </para>
/// <para>
/// It logs a step a second, which the scheduler's <c>UseExecutionLogCapture()</c> keeps: Currently Executing's
/// <em>Show log</em> follows the lines as they come, and Execution Detail shows all twenty once the run ends.
/// </para>
/// </remarks>
[DisallowConcurrentExecution]
public sealed class DemoBlockingJob : IJob
{
    private const int Steps = 20;

    private readonly ILogger<DemoBlockingJob> logger;

    public DemoBlockingJob(ILogger<DemoBlockingJob> logger)
    {
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        for (int step = 1; step <= Steps; step++)
        {
            logger.LogInformation("Step {Step} of {Steps}", step, Steps);

            // Watches the token, so Interrupt stops it at once.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}
