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

#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// A running firing's log read in the process running it (#4437): the lines after a cursor while it runs, and
/// what a node says of a firing it does not run, a scheduler that captures nothing, and a request out of range.
/// </summary>
[NonParallelizable]
public sealed class FireInstanceLogSchedulerTest
{
    private const string Node = "node-one";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(20);
    private static readonly JobKey jobKey = new("gated", "running-log");

    private ServiceProvider? container;

    [SetUp]
    public void SetUp()
    {
        GatedJob.Reset();
    }

    [TearDown]
    public async Task TearDown()
    {
        // Released first, whatever the test did, or the shutdown below would wait for the job for ever.
        GatedJob.ReleaseAll();
        if (container is not null)
        {
            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Shutdown(waitForJobsToComplete: true);
            await container.DisposeAsync();
            container = null;
        }
    }

    [Test]
    public async Task TheLinesAfterTheCursorAreReadWhileTheFiringRunsAndItsEndIsSaid()
    {
        IScheduler scheduler = await Start(capture: true);
        string firing = await RunGated(scheduler);

        FireInstanceLog first = await FireInstanceLogs.Read(scheduler, Request(firing, after: 0));
        first.State.Should().Be(FireInstanceLogState.Running);
        first.SchedulerInstanceId.Should().Be(Node, "the node that answered is what tells an ended firing from one elsewhere");
        Messages(first).Should().Equal(["line 1", "line 2", "line 3"]);
        first.Next.Should().Be(3);
        first.HasMore.Should().BeFalse();

        GatedJob.More.TrySetResult();
        await GatedJob.LoggedFive.Task.WaitAsync(waitLimit);

        FireInstanceLog second = await FireInstanceLogs.Read(scheduler, Request(firing, after: first.Next));
        Messages(second).Should().Equal(["line 4", "line 5"], "only the lines the reader does not have yet");
        second.Next.Should().Be(5);

        GatedJob.Release.TrySetResult();
        await Ended(scheduler);

        FireInstanceLog ended = await FireInstanceLogs.Read(scheduler, Request(firing, after: second.Next));
        ended.State.Should().Be(FireInstanceLogState.NotRunning);
        ended.SchedulerInstanceId.Should().Be(Node, "the firing's own node says it runs it no more: it ended");
        ended.Entries.Should().BeEmpty();
        ended.Next.Should().Be(5, "the cursor comes back as it went");
    }

    /// <summary>
    /// The design guard of <c>CapturesExecutionLog</c>: a firing of a scheduler that does not capture has no buffer,
    /// as a firing that has not reached the capture middleware yet has none, and only the scheduler can tell them apart.
    /// </summary>
    [Test]
    public async Task ASchedulerThatDoesNotCaptureSaysSoRatherThanThatNothingWasLogged()
    {
        IScheduler scheduler = await Start(capture: false);
        string firing = await RunGated(scheduler);

        FireInstanceLog log = await FireInstanceLogs.Read(scheduler, Request(firing, after: 0));

        log.State.Should().Be(FireInstanceLogState.NotCaptured,
            "a reader told Running with no lines would wait for lines that never come, for as long as the firing runs");
        log.SchedulerInstanceId.Should().Be(Node);
        log.Entries.Should().BeEmpty();
    }

    [Test]
    public async Task AFiringThisNodeDoesNotRunIsNotRunningUnderThisNode()
    {
        IScheduler scheduler = await Start(capture: true);

        FireInstanceLog log = await FireInstanceLogs.Read(scheduler, new FireInstanceLogRequest("no-such-firing", 4, 500, "another-node"));

        log.State.Should().Be(FireInstanceLogState.NotRunning);
        log.SchedulerInstanceId.Should().Be(Node, "a node answers for itself, whatever node the request names");
        log.Next.Should().Be(4);
    }

    [TestCase(-1, 500, "after must be 0 or more, was -1*")]
    [TestCase(0, 0, "take must be from 1 to 1000, was 0*")]
    [TestCase(0, 1001, "take must be from 1 to 1000, was 1001*")]
    public async Task ACursorOrAPageSizeOutOfRangeIsRefused(long after, int take, string message)
    {
        IScheduler scheduler = await Start(capture: true);

        Func<Task> read = async () => await FireInstanceLogs.Read(scheduler, new FireInstanceLogRequest("any", after, take, null));
        Func<Task> wire = async () => await SchedulerOperations.GetFireInstanceLog(scheduler, "any", after, take, null, CancellationToken.None);

        await read.Should().ThrowAsync<ArgumentException>().WithMessage(message);
        await wire.Should().ThrowAsync<InvalidRequestException>().WithMessage(message.TrimEnd('*'),
            "the wire refuses it with the same words, as a 400");
    }

    [Test]
    public async Task TheContainersSchedulerAndADecoratorOverItReadTheSameLines()
    {
        IScheduler scheduler = await Start(capture: true);
        string firing = await RunGated(scheduler);

        IScheduler injected = container!.GetRequiredService<IScheduler>();
        injected.Should().NotBeSameAs(scheduler, "the premise: the container's IScheduler is the handle that resolves it");

        FireInstanceLog throughHandle = await FireInstanceLogs.Read(injected, Request(firing, after: 1));
        FireInstanceLog throughDecorator = await FireInstanceLogs.Read(new Decorator(new Decorator(injected)), Request(firing, after: 1));

        Messages(throughHandle).Should().Equal(["line 2", "line 3"]);
        Messages(throughDecorator).Should().Equal(Messages(throughHandle), "a decorator forwards to the scheduler it ends in");
        throughDecorator.SchedulerInstanceId.Should().Be(Node);
    }

    [Test]
    public async Task ASchedulerThatCannotReadARunningLogIsRefused()
    {
        IScheduler handWritten = A.Fake<IScheduler>();
        A.CallTo(() => handWritten.SchedulerName).Returns("hand-written");

        Func<Task> direct = async () => await FireInstanceLogs.Read(handWritten, Request("any", after: 0));
        Func<Task> decorated = async () => await FireInstanceLogs.Read(new Decorator(handWritten), Request("any", after: 0));

        await direct.Should().ThrowAsync<NotSupportedException>().WithMessage("Scheduler 'hand-written' cannot read a running firing's log*");
        await decorated.Should().ThrowAsync<NotSupportedException>().WithMessage("Scheduler 'hand-written'*",
            "there is nothing to compose it from: the lines are in the process running the firing");
    }

    private async Task<IScheduler> Start(bool capture)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = "running-log-" + Guid.NewGuid().ToString("N");
                options.InstanceId = Node;
            });

            if (capture)
            {
                q.UseExecutionLogCapture();
            }
        });

        container = services.BuildServiceProvider();
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        return scheduler;
    }

    private static async Task<string> RunGated(IScheduler scheduler)
    {
        await scheduler.AddJob(JobBuilder.Create<GatedJob>().WithIdentity(jobKey).StoreDurably().Build());
        await scheduler.TriggerJob(jobKey);
        return await GatedJob.LoggedThree.Task.WaitAsync(waitLimit);
    }

    private static async Task Ended(IScheduler scheduler)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + waitLimit;
        while ((await scheduler.GetMetadata()).LocalExecutingJobs > 0)
        {
            DateTimeOffset.UtcNow.Should().BeBefore(deadline, "the job was released and returns at once");
            await Task.Delay(20);
        }
    }

    private static FireInstanceLogRequest Request(string firing, long after)
    {
        return new FireInstanceLogRequest(firing, after, FireInstanceLogs.DefaultTake, SchedulerInstanceId: null);
    }

    private static string[] Messages(FireInstanceLog log)
    {
        return [.. log.Entries.Select(entry => entry[(entry.IndexOf(": ", StringComparison.Ordinal) + 2)..])];
    }

    private sealed class Decorator(IScheduler inner) : DelegatingScheduler(inner);

    /// <summary>
    /// Logs three lines, waits to be told to go on, logs two more, and runs until it is released.
    /// </summary>
    public sealed class GatedJob : IJob
    {
        private readonly ILogger<GatedJob> logger;

        public GatedJob(ILogger<GatedJob> logger)
        {
            this.logger = logger;
        }

        /// <summary>The firing's fire instance id, once it has logged its first three lines.</summary>
        public static TaskCompletionSource<string> LoggedThree { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TaskCompletionSource More { get; private set; } = New();

        public static TaskCompletionSource LoggedFive { get; private set; } = New();

        public static TaskCompletionSource Release { get; private set; } = New();

        public static void Reset()
        {
            LoggedThree = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            More = New();
            LoggedFive = New();
            Release = New();
        }

        public static void ReleaseAll()
        {
            More.TrySetResult();
            Release.TrySetResult();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            for (int line = 1; line <= 3; line++)
            {
                logger.LogInformation("line {Line}", line);
            }

            LoggedThree.TrySetResult(context.FireInstanceId);
            await More.Task.WaitAsync(waitLimit, cancellationToken);

            for (int line = 4; line <= 5; line++)
            {
                logger.LogInformation("line {Line}", line);
            }

            LoggedFive.TrySetResult();
            await Release.Task.WaitAsync(waitLimit, cancellationToken);
        }

        private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
