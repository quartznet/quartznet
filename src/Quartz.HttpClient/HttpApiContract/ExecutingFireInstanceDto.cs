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

namespace Quartz.HttpApiContract;

internal sealed record ExecutingFireInstanceDto(
    string FireInstanceId,
    string TriggerName,
    string TriggerGroup,
    string JobName,
    string JobGroup,
    string SchedulerInstanceId,
    DateTimeOffset FireTimeUtc,
    DateTimeOffset? ScheduledFireTimeUtc)
{
    public static ExecutingFireInstanceDto Create(ExecutingFireInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return new ExecutingFireInstanceDto(
            FireInstanceId: instance.FireInstanceId,
            TriggerName: instance.TriggerKey.Name,
            TriggerGroup: instance.TriggerKey.Group,
            JobName: instance.JobKey.Name,
            JobGroup: instance.JobKey.Group,
            SchedulerInstanceId: instance.SchedulerInstanceId,
            FireTimeUtc: instance.FireTimeUtc,
            ScheduledFireTimeUtc: instance.ScheduledFireTimeUtc
        );
    }

    public ExecutingFireInstance AsExecutingFireInstance()
    {
        return new ExecutingFireInstance
        {
            FireInstanceId = FireInstanceId,
            TriggerKey = new TriggerKey(TriggerName, TriggerGroup),
            JobKey = new JobKey(JobName, JobGroup),
            SchedulerInstanceId = SchedulerInstanceId,
            FireTimeUtc = FireTimeUtc,
            ScheduledFireTimeUtc = ScheduledFireTimeUtc
        };
    }
}
