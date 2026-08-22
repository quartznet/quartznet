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
}