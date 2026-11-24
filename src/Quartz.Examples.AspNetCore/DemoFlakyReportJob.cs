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
/// A job with one healthy schedule and one failing one, used to show each trigger's own last run and failing
/// count on the Triggers page: it fails when the trigger that fired it says <c>fail</c>.
/// </summary>
public sealed class DemoFlakyReportJob : IJob
{
    /// <summary>The trigger data key that makes a firing fail.</summary>
    public const string FailKey = "fail";

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.Trigger.JobDataMap.TryGetBoolean(FailKey, out bool fail) && fail)
        {
            throw new JobExecutionException(
                $"Intentional demo failure: the report source is down for {context.Trigger.Key}, and only for it.");
        }

        return default;
    }
}
