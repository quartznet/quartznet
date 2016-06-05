using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// A <see cref="IJobFactory" /> that returns a fixed job regardless of the job details provided. 
    /// The single job that is returned may be passed in the constructor or changed through the <see cref="Job" /> property
    /// </summary>
    public class FixedJobFactory : IJobFactory
    {
        /// <summary>
        /// If using this empty constructor make sure to set the <see cref="Job" /> property before asking for jobs
        /// </summary>
        public FixedJobFactory()
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="job">The fixed job that will be returned by the job factory</param>
        public FixedJobFactory(IJob job)
        {
            Job = job;
        }

        /// <summary>
        /// The single fixed job to be returned
        /// </summary>
        public IJob Job { get; set; }

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
	    /// <param name="bundle">Not used at all by this job factory</param>
	    /// <param name="scheduler"></param>
	    /// <returns>The job of the <see cref="Job" /> property</returns>
	    /// <throws>  <see cref="SchedulerException" /> if the the <see cref="Job" /> property is null</throws>
        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            if (Job == null)
            {
                SchedulerException se = new SchedulerException("Job property null");
                throw se;
            }
            return Job;
        }

        /// <summary>
	    /// Allows the job factory to destroy/cleanup the job if needed. 	  
	    /// </summary>
        public void ReturnJob(IJob job)
        {
            var disposable = job as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
            }
        }
    }
}
