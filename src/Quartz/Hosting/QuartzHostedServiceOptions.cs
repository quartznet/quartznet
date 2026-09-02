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
/// How <see cref="QuartzHostedService" /> starts and stops one scheduler.
/// </summary>
/// <remarks>
/// Named options, one instance per scheduler name, so a host running several schedulers can start
/// each on its own terms.
/// </remarks>
public sealed class QuartzHostedServiceOptions
{
    /// <summary>
    /// If <see langword="true" /> the scheduler will not allow shutdown process
    /// to return until all currently executing jobs have completed.
    /// </summary>
    public bool WaitForJobsToComplete { get; set; }

    /// <summary>
    /// <para>
    /// If not <see langword="null" /> the scheduler will start after specified delay.
    /// </para>
    /// <para>
    /// If <see cref="AwaitApplicationStarted"/> is true, the delay starts when application startup completes.
    /// </para>
    /// </summary>
    public TimeSpan? StartDelay { get; set; }

    /// <summary>
    /// If true (default), jobs will not be started until application startup completes.
    /// This avoids the running of jobs <em>during</em> application startup.
    /// </summary>
    public bool AwaitApplicationStarted { get; set; } = true;

    /// <summary>
    /// If <see langword="true" /> (the default) the hosted service starts the scheduler. Set it to
    /// <see langword="false" /> to have the scheduler built, initialized and bound but left in
    /// <see cref="SchedulerStatus.Created" />, for the application to start when it is ready.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A library that owns its own leader election, or a module that has work to do before anything
    /// may fire, wants the container to produce a scheduler without the host pressing start. The
    /// scheduler is still created and bound, so <see cref="ISchedulerRegistry" />, the dashboard and
    /// the HTTP API all see it; it simply is not running until something calls
    /// <see cref="IScheduler.Start" />.
    /// </para>
    /// <para>
    /// This wins over <see cref="AwaitApplicationStarted" /> and <see cref="StartDelay" />: both
    /// describe <em>when</em> the hosted service starts a scheduler, and it does not start this one at
    /// all.
    /// </para>
    /// <para>
    /// Shutdown is unaffected. The hosted service shuts down every scheduler it created, started or
    /// not, so opting out of the start is not opting out of the stop.
    /// </para>
    /// </remarks>
    public bool AutoStart { get; set; } = true;
}