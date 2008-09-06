using System;
using System.Collections;

using NUnit.Framework;

using Quartz.Job;
using Quartz.Spi;

namespace Quartz.Tests
{
	/// <summary>
	/// Utility class for tests.
	/// </summary>
	public class TestUtil
	{
		private TestUtil()
		{
		}

		public static void AssertCollectionEquality(IList col1, IList col2)
		{
			Assert.AreEqual(col1.Count, col2.Count, "Collection sizes differ");
			for (int i = 0; i < col1.Count; ++i)
			{
				Assert.AreEqual(col1[i], col2[i], string.Format("Collection items differ at index {0}: {1} vs {2}", i, col1[i], col2[i]));
			}
		}

        /// <summary>
        /// Creates the minimal fired bundle with job detail that has
        /// given job type.
        /// </summary>
        /// <param name="jobType">Type of the job.</param>
        /// <param name="trigger">The trigger.</param>
        /// <returns>Minimal TriggerFiredBundle</returns>
        public static TriggerFiredBundle CreateMinimalFiredBundleWithTypedJobDetail(Type jobType, Trigger trigger)
        {
            JobDetail jd = new JobDetail("jobName", "jobGroup", jobType);
            TriggerFiredBundle bundle = new TriggerFiredBundle(jd, trigger, null, false, null, null, null, null);
            return bundle;
        }

        public static TriggerFiredBundle NewMinimalTriggerFiredBundle()
        {
            JobDetail jd = new JobDetail("jobName", "jobGroup", typeof(NoOpJob));
            SimpleTrigger trigger = new SimpleTrigger("triggerName", "triggerGroup");
            TriggerFiredBundle retValue = new TriggerFiredBundle(jd, trigger, null, false, null, null, null, null);

            return retValue;
        }

        public static JobExecutionContext NewJobExecutionContextFor(IJob job)
        {
            return new JobExecutionContext(null, NewMinimalTriggerFiredBundle(), job);
        }
	}
}
