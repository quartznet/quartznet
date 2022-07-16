#nullable disable

using BenchmarkDotNet.Attributes;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Benchmark
{
    [MemoryDiagnoser]
    public class JobExecutionContextImplBenchmark
    {
        private IJobDetail _jobDetailDataMapEmpty;
        private IJobDetail _jobDetailDataMapNotEmpty;
        private IOperableTrigger _triggerDataMapEmpty;
        private IOperableTrigger _triggerDataMapNotEmpty;
        private TriggerFiredBundle _triggerFiredBundleJobDataMapEmptyAndTriggerDataMapEmpty;
        private TriggerFiredBundle _triggerFiredBundleJobDataMapNotEmptyAndTriggerDataMapEmpty;
        private TriggerFiredBundle _triggerFiredBundleJobDataMapNotEmptyAndTriggerDataMapNotEmpty;
        private TriggerFiredBundle _triggerFiredBundleJobDataMapEmptyAndTriggerDataMapNotEmpty;
        private StdScheduler _scheduler;
        private NoOpJob _job;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var triggerBuilder = TriggerBuilder.Create();
            var jobBuilder = JobBuilder.Create();

            _jobDetailDataMapEmpty = jobBuilder.OfType<NoOpJob>()
                                               .WithIdentity("Empty")
                                               .Build();
            _jobDetailDataMapNotEmpty = jobBuilder.OfType<NoOpJob>()
                                                  .WithIdentity("NotEmpty")
                                                  .UsingJobData("Mutex", "Yes")
                                                  .UsingJobData("LongRunning", "No")
                                                  .Build();
            _triggerDataMapEmpty = (IOperableTrigger) triggerBuilder.WithIdentity("Empty").Build();
            _triggerDataMapNotEmpty = (IOperableTrigger) triggerBuilder.WithIdentity("NotEmpty")
                                                                       .UsingJobData("Foo", "Bar")
                                                                       .UsingJobData("Bar", "Foo")
                                                                       .Build();

            _triggerFiredBundleJobDataMapEmptyAndTriggerDataMapEmpty = CreateTriggerFiredBundle(_jobDetailDataMapEmpty, _triggerDataMapEmpty);
            _triggerFiredBundleJobDataMapEmptyAndTriggerDataMapNotEmpty = CreateTriggerFiredBundle(_jobDetailDataMapEmpty, _triggerDataMapNotEmpty);
            _triggerFiredBundleJobDataMapNotEmptyAndTriggerDataMapEmpty = CreateTriggerFiredBundle(_jobDetailDataMapNotEmpty, _triggerDataMapEmpty);
            _triggerFiredBundleJobDataMapNotEmptyAndTriggerDataMapNotEmpty = CreateTriggerFiredBundle(_jobDetailDataMapNotEmpty, _triggerDataMapNotEmpty);

            _scheduler = new StdScheduler(CreateQuartzScheduler("x", "1", 5));
            _job = new NoOpJob();
        }

        [Benchmark]
        public JobExecutionContextImpl Ctor_JobDataMapEmpty_TriggerDataMapEmpty()
        {
            return new JobExecutionContextImpl(_scheduler, _triggerFiredBundleJobDataMapEmptyAndTriggerDataMapEmpty, _job);
        }

        [Benchmark]
        public JobExecutionContextImpl Ctor_JobDataMapEmpty_TriggerDataMapNotEmpty()
        {
            return new JobExecutionContextImpl(_scheduler, _triggerFiredBundleJobDataMapEmptyAndTriggerDataMapNotEmpty, _job);
        }

        [Benchmark]
        public JobExecutionContextImpl Ctor_JobDataMapNotEmpty_TriggerDataMapEmpty()
        {
            return new JobExecutionContextImpl(_scheduler, _triggerFiredBundleJobDataMapNotEmptyAndTriggerDataMapEmpty, _job);
        }

        [Benchmark]
        public JobExecutionContextImpl Ctor_JobDataMapNotEmpty_TriggerDataMapNotEmpty()
        {
            return new JobExecutionContextImpl(_scheduler, _triggerFiredBundleJobDataMapNotEmptyAndTriggerDataMapNotEmpty, _job);
        }

        private static TriggerFiredBundle CreateTriggerFiredBundle(IJobDetail jobDetail, IOperableTrigger trigger)
        {
            return new TriggerFiredBundle(jobDetail,
                                          trigger,
                                          null,
                                          false,
                                          DateTimeOffset.Now,
                                          DateTimeOffset.Now,
                                          DateTimeOffset.Now,
                                          DateTimeOffset.Now);
        }

        private static QuartzScheduler CreateQuartzScheduler(string name, string instanceId, int threadCount)
        {
            QuartzSchedulerResources res = new QuartzSchedulerResources
                {
                    Name = name,
                    InstanceId = instanceId,
                    ThreadPool = new DefaultThreadPool { MaxConcurrency = threadCount },
                    JobStore = new RAMJobStore(),
                    MaxBatchSize = threadCount,
                    BatchTimeWindow = TimeSpan.Zero
                };

            return new QuartzScheduler(res);
        }
    }
}
