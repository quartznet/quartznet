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

namespace Quartz;

internal static class SchedulerStatusExtensions
{
    /// <summary>
    /// Collapses a scheduler's three lifecycle flags into one <see cref="SchedulerStatus" />.
    /// </summary>
    /// <remarks>
    /// Precedence matters and is the reason this lives in one place: a shut-down scheduler also reports
    /// <see cref="IScheduler.IsStarted" />, so "shutdown" has to be asked first. Reading the flags in
    /// another order is how the HTTP API and the dashboard's in-process client came to disagree about
    /// what a running scheduler is called.
    /// </remarks>
    public static SchedulerStatus GetStatus(this IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        if (scheduler.IsShutdown)
        {
            return SchedulerStatus.Shutdown;
        }

        if (scheduler.InStandbyMode)
        {
            return SchedulerStatus.Standby;
        }

        if (scheduler.IsStarted)
        {
            return SchedulerStatus.Running;
        }

        return SchedulerStatus.Unknown;
    }
}
