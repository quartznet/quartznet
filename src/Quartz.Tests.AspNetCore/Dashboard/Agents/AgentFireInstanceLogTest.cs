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

using Microsoft.Extensions.Logging;

using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// A running firing's log through an agent (#4437): read in the worker's process, where the firing runs, and an
/// operation the worker may refuse by name, which <c>IsOperationAllowed</c> sees as every route's.
/// </summary>
[NonParallelizable]
public sealed class AgentFireInstanceLogTest
{
    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);
    private static readonly JobKey Job = new("held", "agent-running-log");

    [SetUp]
    public void SetUp()
    {
        HeldLoggingJob.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        HeldLoggingJob.Release.TrySetResult();
    }

    [Test]
    public async Task TheWorkerReadsTheLinesItsFiringLogged()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", clock: TimeProvider.System, configureQuartz: quartz => quartz.UseExecutionLogCapture());
        await worker.Scheduler.AddJob(JobBuilder.Create<HeldLoggingJob>().WithIdentity(Job).StoreDurably().Build());
        await worker.Scheduler.TriggerJob(Job);
        string firing = await HeldLoggingJob.Logged.Task.WaitAsync(waitLimit);

        FireInstanceLogDto log = await dashboard.Client.GetFireInstanceLog(worker.Key, new DashboardFireInstanceLogQuery
        {
            FireInstanceId = firing,
            After = 1,
            SchedulerInstanceId = worker.Scheduler.SchedulerInstanceId,
        });

        log.Status.Should().Be(FireInstanceLogStatus.Running);
        log.SchedulerInstanceId.Should().Be(worker.Scheduler.SchedulerInstanceId, "the worker answered, for its own node");
        log.Entries.Should().ContainSingle().Which.Should().EndWith("the second line");
        log.Next.Should().Be(2);
    }

    [Test]
    public async Task AWorkerThatRefusesTheOperationIsRefusedByName()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.IsOperationAllowed = route => route != "GetFireInstanceLog");

        Func<Task> read = () => dashboard.Client.GetFireInstanceLog(worker.Key, new DashboardFireInstanceLogQuery { FireInstanceId = "any" }).AsTask();

        (await read.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain("does not accept GetFireInstanceLog",
                "an agent that refuses GetExecution to keep captured logs from a caller refuses this read by name too");
    }

    /// <summary>Logs two lines and runs until it is released.</summary>
    public sealed class HeldLoggingJob : IJob
    {
        private readonly ILogger<HeldLoggingJob> logger;

        public HeldLoggingJob(ILogger<HeldLoggingJob> logger)
        {
            this.logger = logger;
        }

        /// <summary>The firing's fire instance id, once it has logged.</summary>
        public static TaskCompletionSource<string> Logged { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TaskCompletionSource Release { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Logged = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("the first line");
            logger.LogInformation("the second line");
            Logged.TrySetResult(context.FireInstanceId);
            await Release.Task.WaitAsync(waitLimit, cancellationToken);
        }
    }
}
