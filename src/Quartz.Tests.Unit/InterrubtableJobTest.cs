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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;

using NUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Test job interruption.
    /// </summary>
    [TestFixture]
    public class InterruptableJobTest
    {
        private static readonly ManualResetEvent sync = new ManualResetEvent(false);

        public class TestInterruptableJob : IInterruptableJob
        {
            public static bool interrupted;

            public void Execute(IJobExecutionContext context)
            {
                Console.WriteLine("TestInterruptableJob is executing.");
                try
                {
                    sync.Set(); // wait for test thread to notice the job is now running
                }
                catch (ThreadInterruptedException)
                {
                }

                interrupted = false;
                for (int i = 0; i < 100; i++)
                {
                    if (interrupted)
                    {
                        break;
                    }
                    try
                    {
                        Thread.Sleep(50); // simulate being busy for a while, then checking interrupted flag...
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }
                try
                {
                    Console.WriteLine("TestInterruptableJob exiting with interrupted = " + interrupted);
                    sync.WaitOne();
                }
                catch (ThreadInterruptedException)
                {
                }
            }

            public void Interrupt()
            {
                interrupted = true;
                Console.WriteLine("TestInterruptableJob.interrupt() called.");
            }
        }

        [Test]
        public void TestJobInterruption()
        {
            // create a simple scheduler

            NameValueCollection config = new NameValueCollection();
            config["quartz.scheduler.instanceName"] = "InterruptableJobTest_Scheduler";
            config["quartz.scheduler.instanceId"] = "AUTO";
            config["quartz.threadPool.threadCount"] = "2";
            config["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool";
            IScheduler sched = new StdSchedulerFactory(config).GetScheduler();
            sched.Start();

            // add a job with a trigger that will fire immediately

            IJobDetail job = JobBuilder.Create<TestInterruptableJob>()
                .WithIdentity("j1")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t1")
                .ForJob(job)
                .StartNow()
                .Build();

            sched.ScheduleJob(job, trigger);

            sync.WaitOne(); // make sure the job starts running...

            IList<IJobExecutionContext> executingJobs = sched.GetCurrentlyExecutingJobs();

            Assert.AreEqual(1, executingJobs.Count, "Number of executing jobs should be 1 ");

            IJobExecutionContext jec = executingJobs[0];

            bool interruptResult = sched.Interrupt(jec.FireInstanceId);

            sync.WaitOne(); // wait for the job to terminate

            Assert.IsTrue(interruptResult, "Expected successful result from interruption of job ");
            Assert.IsTrue(TestInterruptableJob.interrupted, "Expected interrupted flag to be set on job class ");

            sched.Clear();
            sched.Shutdown();
        }
    }
}