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

namespace Quartz.Examples.AspNetCore;

/// <summary>
/// A job that runs for 20 seconds and may not run beside itself, triggered every 5 seconds, so its trigger is
/// <see cref="TriggerState.Blocked" /> most of the time: what the dashboard's "Why isn't it firing?" shows, with the
/// firing that holds the job and <em>Interrupt</em>.
/// </summary>
[DisallowConcurrentExecution]
public sealed class DemoBlockingJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Watches the token, so Interrupt stops it at once.
        await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
    }
}
