using System;
using System.Diagnostics;

using Quartz.Impl.Matchers;
using Quartz.Listener;

namespace Quartz.Core
{
    public class SampledStatisticsImpl : SchedulerListenerSupport, ISampledStatistics, IJobListener, ISchedulerListener
    {
        private readonly IScheduler scheduler;

        private const string ListenerName = "QuartzSampledStatistics";

        private readonly PerformanceCounter jobsScheduledCount;
        private readonly PerformanceCounter jobsExecutingCount;
        private readonly PerformanceCounter jobsCompletedCount;
        private const string CategoryName = "Quartz.NET";
        private const string CounterNameNumberOfJobsScheduled = "# jobs scheduled";
        private const string CounterNameJobsExecuting = "# jobs executing";
        private const string CounterNameJobsCompleted = "# jobs completed";

        public SampledStatisticsImpl(IScheduler scheduler)
        {
            this.scheduler = scheduler;

            try
            {
                EnsureCounters();
            }
            catch (Exception ex)
            {
                Log.Error("Unable check/create performance counters, plugin disabled, exception: " + ex.Message, ex);
                return;
            }

            jobsScheduledCount = CreateSampledCounter(CounterNameNumberOfJobsScheduled);
            jobsExecutingCount = CreateSampledCounter(CounterNameJobsExecuting);
            jobsCompletedCount = CreateSampledCounter(CounterNameJobsCompleted);

            scheduler.AddSchedulerListener(this);
            scheduler.AddJobListener(this, EverythingMatcher<>.matchAllJobs());
        }

        private void EnsureCounters()
        {
            if (!PerformanceCounterCategory.Exists(CategoryName))
            {
                CounterCreationDataCollection counters = new CounterCreationDataCollection();

                counters.Add(CreateCountingCounterCreationData(CounterNameNumberOfJobsScheduled, "Total number of jobs that have been scheduled"));
                counters.Add(CreateCountingCounterCreationData(CounterNameJobsExecuting, "Total number of jobs that have started executing"));
                counters.Add(CreateCountingCounterCreationData(CounterNameJobsCompleted, "Total number of jobs that have completed"));

                // create new category with counters
                PerformanceCounterCategory.Create(CategoryName, "Quartz.NET Performance Counters", PerformanceCounterCategoryType.MultiInstance, counters);
            }
        }

        private static CounterCreationData CreateCountingCounterCreationData(string counterName, string counterHelp)
        {
            CounterCreationData totalOps = new CounterCreationData();
            totalOps.CounterName = counterName;
            totalOps.CounterHelp = counterHelp;
            totalOps.CounterType = PerformanceCounterType.NumberOfItems32;
            return totalOps;
        }

        private PerformanceCounter CreateSampledCounter(string counterName)
        {
            // create counters to work with

            PerformanceCounter counter = new PerformanceCounter();
            counter.CategoryName = CategoryName;
            counter.InstanceName = scheduler.SchedulerInstanceId;
            counter.CounterName = counterName;
            counter.MachineName = ".";
            counter.ReadOnly = false;
            return counter;
        }


        public long JobsCompletedMostRecentSample
        {
            get { return (long) jobsCompletedCount.NextValue(); }
        }

        public long JobsExecutingMostRecentSample
        {
            get { return (long) jobsExecutingCount.NextValue(); }
        }

        public long JobsScheduledMostRecentSample
        {
            get { return (long) jobsScheduledCount.NextValue(); }
        }

        public string Name
        {
            get { return ListenerName; }
        }

        public override void JobScheduled(Trigger trigger)
        {
            jobsScheduledCount.Increment();
        }

        public void JobExecutionVetoed(JobExecutionContext context)
        {
        }

        public void JobToBeExecuted(JobExecutionContext context)
        {
            jobsExecutingCount.Increment();
        }

        public void JobWasExecuted(JobExecutionContext context,
                                   JobExecutionException jobException)
        {
            jobsCompletedCount.Increment();
        }

        public override void JobAdded(JobDetail jobDetail)
        {
        }

        public override void JobDeleted(string jobName, string groupName)
        {
        }
    }
}