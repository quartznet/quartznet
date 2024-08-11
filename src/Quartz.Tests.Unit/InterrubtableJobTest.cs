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

using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// Test job interruption.
/// </summary>
[TestFixture]
public class InterruptableJobTest
{
    private static readonly ManualResetEvent started = new(false);
    private static readonly ManualResetEvent ended = new(false);

    [OneTimeTearDown]
    public void TearDown()
    {
        started.Dispose();
        ended.Dispose();
    }
    
    public class TestInterruptableJob : IJob
    {
        public static bool interrupted;

        public async ValueTask Execute(IJobExecutionContext context)
        {
            // Console.WriteLine("TestInterruptableJob is executing.");
            try
            {
                started.Set(); // wait for test thread to notice the job is now running
            }
            catch (ThreadInterruptedException)
            {
            }

            interrupted = false;
            for (int i = 0; i < 100; i++)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    interrupted = true;
                    break;
                }
                await Task.Delay(50); // simulate being busy for a while, then checking interrupted flag...
            }
            try
            {
                // Console.WriteLine("TestInterruptableJob exiting with interrupted = " + interrupted);
                ended.Set();
            }
            catch (ThreadInterruptedException)
            {
            }
        }
    }

    [Test]
    public async Task TestJobInterruption()
    {
        // create a simple scheduler

        NameValueCollection config = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "InterruptableJobTest_Scheduler",
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.threadPool.threadCount"] = "2",
            ["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        IScheduler sched = await new StdSchedulerFactory(config).GetScheduler();
        await sched.Start();

        // add a job with a trigger that will fire immediately

        IJobDetail job = JobBuilder.Create<TestInterruptableJob>()
            .WithIdentity("j1")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .Build();

        await sched.ScheduleJob(job, trigger);

        started.WaitOne(); // make sure the job starts running...

        var executingJobs = await sched.GetCurrentlyExecutingJobs();

        Assert.That(executingJobs, Has.Count.EqualTo(1), "Number of executing jobs should be 1 ");

        IJobExecutionContext jec = executingJobs.First();

        bool interruptResult = await sched.Interrupt(jec.FireInstanceId);

        ended.WaitOne(); // wait for the job to terminate

        Assert.Multiple(() =>
        {
            Assert.That(interruptResult, Is.True, "Expected successful result from interruption of job ");
            Assert.That(TestInterruptableJob.interrupted, Is.True, "Expected interrupted flag to be set on job class ");
        });

        await sched.Clear();
        await sched.Shutdown();
    }
}