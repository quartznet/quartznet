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

/// <summary>
/// What one call to the <c>ScheduleJob&lt;TJob, TInput&gt;</c> one-liners arranged: the trigger that was
/// stored, and when it will first fire.
/// </summary>
/// <remarks>
/// <para>
/// Two members, because the one-liner wraps
/// <see cref="IScheduler.ScheduleJob(ITrigger, ScheduleJobOptions, System.Threading.CancellationToken)" />
/// and should not answer less than what it wrapped: the key is the handle, and the fire time is what a
/// caller logging "scheduled for X" would otherwise have to guess from the time it asked for — which is
/// not the same answer once a calendar or a misfire policy has had its say.
/// </para>
/// <para>
/// It is not a query object, and stays two members. Everything else about the firing is a property of the
/// trigger the key names, and <see cref="IScheduler.GetTrigger" /> is how to ask for it.
/// </para>
/// </remarks>
/// <param name="TriggerKey">
/// The key of the trigger that was stored, which is the handle to cancel it with — see
/// <see cref="IScheduler.UnscheduleJob" /> — or to replace it by scheduling the same name again.
/// </param>
/// <param name="FirstFireTimeUtc">
/// The first time at which the trigger will fire, as the store computed it. The same value the
/// <see cref="IScheduler.ScheduleJob(ITrigger, ScheduleJobOptions, System.Threading.CancellationToken)" />
/// overload the one-liner wraps returns.
/// </param>
/// <seealso cref="SchedulerJobExtensions" />
/// <seealso cref="OneOffJobOptions" />
public readonly record struct ScheduledOneOffJob(TriggerKey TriggerKey, DateTimeOffset FirstFireTimeUtc);
