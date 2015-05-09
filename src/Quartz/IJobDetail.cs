#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System;

namespace Quartz
{
    /// <summary>
    /// Conveys the detail properties of a given job instance. 
    /// JobDetails are to be created/defined with <see cref="JobBuilder" />.
    /// </summary>
    /// <remarks>
    /// Quartz does not store an actual instance of a <see cref="IJob" /> type, but
    /// instead allows you to define an instance of one, through the use of a <see cref="IJobDetail" />.
    /// <para>
    /// <see cref="IJob" />s have a name and group associated with them, which
    /// should uniquely identify them within a single <see cref="IScheduler" />.
    /// </para>
    /// <para>
    /// <see cref="ITrigger" /> s are the 'mechanism' by which <see cref="IJob" /> s
    /// are scheduled. Many <see cref="ITrigger" /> s can point to the same <see cref="IJob" />,
    /// but a single <see cref="ITrigger" /> can only point to one <see cref="IJob" />.
    /// </para>
    /// </remarks>
    /// <seealso cref="IJob" />
    /// <seealso cref="DisallowConcurrentExecutionAttribute"/>
    /// <seealso cref="PersistJobDataAfterExecutionAttribute"/>
    /// <seealso cref="Quartz.JobDataMap"/>
    /// <seealso cref="ITrigger"/>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface IJobDetail : ICloneable
    {
        /// <summary>
        /// The key that identifies this jobs uniquely.
        /// </summary>
        JobKey Key { get; }

        /// <summary>
        /// Get or set the description given to the <see cref="IJob" /> instance by its
        /// creator (if any).
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Get or sets the instance of <see cref="IJob" /> that will be executed.
        /// </summary>
        Type JobType { get; }

        /// <summary>
        /// Get or set the <see cref="JobDataMap" /> that is associated with the <see cref="IJob" />.
        /// </summary>
        JobDataMap JobDataMap { get; }

        /// <summary>
        /// Whether or not the <see cref="IJob" /> should remain stored after it is
        /// orphaned (no <see cref="ITrigger" />s point to it).
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </remarks>
        /// <returns> 
        /// <see langword="true" /> if the Job should remain persisted after being orphaned.
        /// </returns>
        bool Durable { get; }

        /// <summary>
        /// Whether the associated Job class carries the <see cref="PersistJobDataAfterExecutionAttribute" />.
        /// </summary>
        /// <seealso cref="PersistJobDataAfterExecutionAttribute" />
        bool PersistJobDataAfterExecution { get; }

        /// <summary>
        /// Whether the associated Job class carries the <see cref="DisallowConcurrentExecutionAttribute" />.
        /// </summary>
        /// <seealso cref="DisallowConcurrentExecutionAttribute"/>
        bool ConcurrentExecutionDisallowed { get; }

        /// <summary>
        /// Set whether or not the <see cref="IScheduler" /> should re-Execute
        /// the <see cref="IJob" /> if a 'recovery' or 'fail-over' situation is
        /// encountered.
        /// </summary>
        /// <remarks>
        /// If not explicitly set, the default value is <see langword="false" />.
        /// </remarks>
        /// <seealso cref="IJobExecutionContext.Recovering" />
        bool RequestsRecovery { get; }

        /// <summary>
        /// Get a <see cref="JobBuilder" /> that is configured to produce a 
        /// <see cref="IJobDetail" /> identical to this one.
        /// </summary>
        JobBuilder GetJobBuilder();
    }
}