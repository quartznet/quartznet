using System.Threading;

using MbUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Integration.ExceptionPolicy
{
    [TestFixture]
    public class ExceptionHandlingTest : IntegrationTest
    {
		[SetUp]
		public void SetUp()
		{
			ISchedulerFactory sf = new StdSchedulerFactory();
			sched = sf.GetScheduler();   
		}

        [Test]
        public void ExceptionJobUnscheduleFirinigTrigger()
        {
            sched.Start();
            string jobName = "ExceptionPolicyUnscheduleFrinigTrigger";
            string jobGroup = "ExceptionPolicyUnscheduleFrinigTriggerGroup";
            JobDetail myDesc = new JobDetail(jobName, jobGroup, typeof (ExceptionJob));
            myDesc.Durable = true;
            sched.AddJob(myDesc, false);
            string trigGroup = "ExceptionPolicyFrinigTriggerGroup";
            Trigger trigger = new CronTrigger("trigName", trigGroup, "0/2 * * * * ?");
            trigger.JobName = jobName;
            trigger.JobGroup = jobGroup;

            ExceptionJob.ThrowsException = true;
            ExceptionJob.LaunchCount = 0;
            ExceptionJob.Refire = false;
            ExceptionJob.UnscheduleFiringTrigger = true;
            ExceptionJob.UnscheduleAllTriggers = false;

            sched.ScheduleJob(trigger);

            Thread.Sleep(7*1000);
            sched.DeleteJob(jobName, jobGroup);
            Assert.AreEqual(1, ExceptionJob.LaunchCount,
                            "The job shouldn't have been refired (UnscheduleFiringTrigger)");


            ExceptionJob.LaunchCount = 0;
            ExceptionJob.UnscheduleFiringTrigger = true;
            ExceptionJob.UnscheduleAllTriggers = false;

            sched.AddJob(myDesc, false);
            trigger = new CronTrigger("trigName", trigGroup, "0/2 * * * * ?");
            trigger.JobName = jobName;
            trigger.JobGroup = jobGroup;
            sched.ScheduleJob(trigger);
            trigger = new CronTrigger("trigName1", trigGroup, "0/3 * * * * ?");
            trigger.JobName = jobName;
            trigger.JobGroup = jobGroup;
            sched.ScheduleJob(trigger);
            Thread.Sleep(7*1000);
            sched.DeleteJob(jobName, jobGroup);
            Assert.AreEqual(2, ExceptionJob.LaunchCount,
                            "The job shouldn't have been refired(UnscheduleFiringTrigger)");
        }

        [Test]
        public void ExceptionPolicyRestartImmediately()
        {
            sched.Start();
            string jobName = "ExceptionPolicyRestartJob";
            string jobGroup = "ExceptionPolicyRestartGroup";
            JobDetail exceptionJob = new JobDetail(jobName, jobGroup, typeof (ExceptionJob));
            exceptionJob.Durable = true;
            sched.AddJob(exceptionJob, false);

            ExceptionJob.ThrowsException = true;
            ExceptionJob.Refire = true;
            ExceptionJob.UnscheduleAllTriggers = false;
            ExceptionJob.UnscheduleFiringTrigger = false;
            ExceptionJob.LaunchCount = 0;
            sched.TriggerJob(jobName, jobGroup);

            int i = 10;
            while ((i > 0) && (ExceptionJob.LaunchCount <= 1))
            {
                i--;
                Thread.Sleep(200);
                if (ExceptionJob.LaunchCount > 1)
                {
                    break;
                }
            }
            // to ensure job will not be refired in consequent tests
            // in fact, it would be better to have a separate class
            ExceptionJob.ThrowsException = false;
            sched.DeleteJob(jobName, jobGroup);
            Thread.Sleep(1000);
            Assert.GreaterThan(ExceptionJob.LaunchCount, 1, "The job should have been refired after exception");
        }

        [Test]
        public void ExceptionPolicyNoRestartImmediately()
        {
            sched.Start();
            string jobName = "ExceptionPolicyNoRestartJob";
            string jobGroup = "ExceptionPolicyNoRestartGroup";
            JobDetail exceptionJob = new JobDetail(jobName, jobGroup, typeof (ExceptionJob));
            exceptionJob.Durable = true;
            sched.AddJob(exceptionJob, false);

            ExceptionJob.ThrowsException = true;
            ExceptionJob.Refire = false;
            ExceptionJob.UnscheduleAllTriggers = false;
            ExceptionJob.UnscheduleFiringTrigger = false;
            ExceptionJob.LaunchCount = 0;
            sched.TriggerJob(jobName, jobGroup);

            int i = 10;
            while ((i > 0) && (ExceptionJob.LaunchCount <= 1))
            {
                i--;
                Thread.Sleep(200);
                if (ExceptionJob.LaunchCount > 1)
                {
                    break;
                }
            }
            sched.DeleteJob(jobName, jobGroup);
            Assert.AreEqual(1, ExceptionJob.LaunchCount, "The job should NOT have been refired after exception");
        }
    }
}