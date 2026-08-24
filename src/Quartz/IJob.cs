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
/// The interface to be implemented by classes which represent a 'job' to be
/// performed.
/// </summary>
/// <remarks>
/// An instance is built for each fire by the scheduler's <see cref="Quartz.Extensibility.IJobFactory" />,
/// which by default resolves the job from the dependency-injection container — so a job's dependencies
/// are ordinary constructor parameters, and there need be no parameterless constructor.
/// <see cref="JobDataMap" /> provides a mechanism for 'instance member data' that may be required by
/// some implementations of this interface.
/// </remarks>
/// <seealso cref="IJobDetail" />
/// <seealso cref="JobBuilder" />
/// <seealso cref="DisallowConcurrentExecutionAttribute" />
/// <seealso cref="PersistJobDataAfterExecutionAttribute" />
/// <seealso cref="ITrigger" />
/// <seealso cref="IScheduler" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface IJob
{
    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// fires that is associated with the <see cref="IJob" />.
    /// </summary>
    /// <remarks>
    /// The implementation may wish to set a  result object on the
    /// JobExecutionContext before this method exits.  The result itself
    /// is meaningless to Quartz, but may be informative to
    /// <see cref="IJobListener" />s or
    /// <see cref="ITriggerListener" />s that are watching the job's
    /// execution.
    /// </remarks>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">
    /// Signalled when this execution is interrupted — either by
    /// <see cref="IScheduler.Interrupt(JobKey, CancellationToken)" /> or by the scheduler shutting
    /// down while configured to interrupt running jobs. A long-running job should pass this on to
    /// everything it awaits; a job that ignores it cannot be interrupted, and will hold up shutdown
    /// until it finishes on its own.
    /// <para>
    /// This is the same token as <see cref="IJobExecutionContext.CancellationToken" />, given as a
    /// parameter so that it is impossible to miss and so that the compiler can point out where it
    /// has not been forwarded.
    /// </para>
    /// </param>
    ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default);
}