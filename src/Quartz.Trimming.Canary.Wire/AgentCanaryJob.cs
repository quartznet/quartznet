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

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The job the hub schedules through the agent. It reaches the agent as a type name in a request body
/// down the tunnel, and the agent's scheduler builds it when the hub triggers it.
/// </summary>
public sealed class AgentCanaryJob : IJob
{
    /// <summary>
    /// What the job was handed: the job's own data and the data the hub triggered it with. Signalled by
    /// the job itself, so nothing waits on a sleep.
    /// </summary>
    internal static TaskCompletionSource<WireCanaryRun> Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        Ran.TrySetResult(new WireCanaryRun(
            context.MergedJobDataMap.GetString(WireCanaryRun.PayloadKey),
            context.MergedJobDataMap.GetString(WireCanaryRun.FiredByKey)));

        return default;
    }
}
