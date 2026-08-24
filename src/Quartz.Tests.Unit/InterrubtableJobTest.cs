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

using Quartz.Listeners;

namespace Quartz.Tests.Unit;

/// <summary>
/// Test job interruption.
/// </summary>
[NonParallelizable]
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

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
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
            ["quartz.threadPool.type"] = "Quartz.Impl.DefaultThreadPool",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseProperties(config).BuildScheduler();
        await scheduler.Start();

        // add a job with a trigger that will fire immediately

        IJobDetail job = JobBuilder.Create<TestInterruptableJob>()
            .WithIdentity("j1")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t1")
            .ForJob(job)
            .StartNow()
            .Build();

        var interruptionListener = new JobInterruptedCaptureListener();
        scheduler.ListenerManager.AddSchedulerListener(interruptionListener);

        await scheduler.ScheduleJob(job, trigger);

        started.WaitOne(); // make sure the job starts running...

        var executingJobs = await scheduler.QueryFireInstances(new FireInstanceQuery());

        executingJobs.Items.Should().ContainSingle("one execution is running");

        bool interruptResult = await scheduler.InterruptFireInstance(executingJobs.Items[0].FireInstanceId);

        ended.WaitOne(); // wait for the job to terminate

        Assert.Multiple(() =>
        {
            Assert.That(interruptResult, Is.True, "Expected successful result from interruption of job ");
            Assert.That(TestInterruptableJob.interrupted, Is.True, "Expected interrupted flag to be set on job class ");
        });

        // the notification is awaited inside InterruptFireInstance before it returns, so no waiting is needed here
        interruptionListener.Interrupted.Task.IsCompleted.Should().BeTrue(
            "interrupting by fire instance id should notify scheduler listeners of the interruption");
        (await interruptionListener.Interrupted.Task).Should().Be(job.Key);

        await scheduler.Clear();
        await scheduler.Shutdown();
    }

    private sealed class JobInterruptedCaptureListener : ISchedulerListener
    {
        public TaskCompletionSource<JobKey> Interrupted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask JobInterrupted(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default)
        {
            Interrupted.TrySetResult(jobKey);
            return default;
        }
    }
}