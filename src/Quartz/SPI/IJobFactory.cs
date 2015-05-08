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
using Quartz.Simpl;

namespace Quartz.Spi
{
	/// <summary>
	/// A JobFactory is responsible for producing instances of <see cref="IJob" />
	/// classes.
	/// </summary>
	/// <remarks>
	/// This interface may be of use to those wishing to have their application
	/// produce <see cref="IJob" /> instances via some special mechanism, such as to
	/// give the opportunity for dependency injection.
    /// </remarks>
	/// <seealso cref="IScheduler.JobFactory" />
	/// <seealso cref="SimpleJobFactory" />
	/// <seealso cref="PropertySettingJobFactory" />
	/// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public interface IJobFactory
	{
	    /// <summary> 
	    /// Called by the scheduler at the time of the trigger firing, in order to
	    /// produce a <see cref="IJob" /> instance on which to call Execute.
	    /// </summary>
	    /// <remarks>
	    /// It should be extremely rare for this method to throw an exception -
	    /// basically only the case where there is no way at all to instantiate
	    /// and prepare the Job for execution.  When the exception is thrown, the
	    /// Scheduler will move all triggers associated with the Job into the
	    /// <see cref="TriggerState.Error" /> state, which will require human
	    /// intervention (e.g. an application restart after fixing whatever 
	    /// configuration problem led to the issue with instantiating the Job). 
	    /// </remarks>
	    /// <param name="bundle">
	    ///   The TriggerFiredBundle from which the <see cref="IJobDetail" />
	    ///   and other info relating to the trigger firing can be obtained.
	    /// </param>
        /// <param name="scheduler">a handle to the scheduler that is about to execute the job</param>
	    /// <throws>  SchedulerException if there is a problem instantiating the Job. </throws>
	    /// <returns> the newly instantiated Job
	    /// </returns>
	    IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler);

        /// <summary>
        /// Allows the job factory to destroy/cleanup the job if needed.
        /// </summary>
	    void ReturnJob(IJob job);
	}
}