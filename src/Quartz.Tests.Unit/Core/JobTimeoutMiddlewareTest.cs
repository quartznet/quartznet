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

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// The timeout middleware, through a real scheduler: what a job that runs too long is told, what the
/// trigger is told afterwards, and what a job that declares a budget of its own gets instead.
/// </summary>
/// <remarks>
/// <para>
/// The budgets are milliseconds so a test can sit through them, and nothing here fakes a clock: the
/// point is that the interrupt travels the same path an operator's does, and a substituted clock would
/// stop timing the thing under test.
/// </para>
/// <para>
/// What happened is kept in a recorder resolved from the container rather than in a static, because the
/// jobs and the listeners are built by the container the same way an application's are.
/// </para>
/// </remarks>
public sealed class JobTimeoutMiddlewareTest
{
    /// <summary>
    /// The headline: a job that outstays its budget is interrupted, and the firing is reported as a
    /// failure rather than as the successful completion an interrupt on its own looks like.
    /// </summary>
    [Test]
    public async Task AJobThatOutstaysItsBudgetIsInterruptedAndTheFiringFails()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, typeof(HangingJob), quartz => quartz.AddJobTimeout(TimeSpan.FromMilliseconds(200)));

        recorder.CancellationSeenByJob.Should().BeTrue(
            "the middleware interrupts the firing through the scheduler, which cancels the very token the job "
            + "was handed - a linked token of the middleware's own would have left the context saying something else");

        recorder.JobException.Should().NotBeNull(
            "an interrupt on its own is success-shaped: the run shell classifies a cancellation of the context's "
            + "token as a completed firing, so without the rethrow a timeout would be invisible and unretryable");
        recorder.JobException!.Message.Should().Contain("timed out",
            "the failure has to say it was a timeout rather than merely that something threw");
        recorder.JobException.Message.Should().Contain("00:00:00.2000000",
            "the message names the budget that was spent, because that is the number an operator changes");

        recorder.Instruction.Should().Be(SchedulerInstruction.DeleteTrigger,
            "a timeout is an ordinary failure, so a one-shot trigger with no retry policy and nothing left to "
            + "fire is finalized exactly as it would be after any other one");
    }

    /// <summary>
    /// The other side of it: nothing is interrupted and nothing is reported when the job finishes in
    /// time.
    /// </summary>
    [Test]
    public async Task AJobThatFinishesInTimeIsLeftAlone()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, typeof(QuickJob), quartz => quartz.AddJobTimeout(TimeSpan.FromSeconds(30)));

        recorder.Executions.Should().Be(1);
        recorder.CancellationSeenByJob.Should().BeFalse("the job finished well inside its budget");
        recorder.JobException.Should().BeNull("a job that ran in time did not fail");
    }

    /// <summary>
    /// A scheduler that never registered the middleware has no timeouts, whatever a job type declares.
    /// </summary>
    [Test]
    public async Task WithoutTheMiddlewareAJobsOwnBudgetDoesNothing()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, typeof(ImpatientJob), _ => { });

        recorder.CancellationSeenByJob.Should().BeFalse(
            "the attribute is read by the middleware and by nothing else, so a scheduler that never registered "
            + "one has no timeouts to enforce - which is why AddJobTimeout() with no default has to exist");
        recorder.JobException.Should().BeNull();
    }

    /// <summary>
    /// The per-job budget, declared on the type, beats the scheduler-wide default.
    /// </summary>
    [Test]
    public async Task TheJobsOwnBudgetBeatsTheSchedulerDefault()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, typeof(ImpatientJob), quartz => quartz.AddJobTimeout(TimeSpan.FromSeconds(30)));

        recorder.CancellationSeenByJob.Should().BeTrue(
            "[JobTimeout] on the job type overrides the scheduler-wide default, so the 200 ms the job declared "
            + "decided this firing rather than the 30 seconds the scheduler did");
        recorder.JobException.Should().NotBeNull();
    }

    /// <summary>
    /// A budget of zero is how a job says it has none — the long-running one whose whole point is to run
    /// until it is done.
    /// </summary>
    [Test]
    public async Task AJobDeclaringNoBudgetIsExemptFromTheSchedulerDefault()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, typeof(UnboundedJob), quartz => quartz.AddJobTimeout(TimeSpan.FromMilliseconds(200)));

        recorder.Executions.Should().Be(1);
        recorder.CancellationSeenByJob.Should().BeFalse(
            "[JobTimeout(\"00:00:00\")] exempts a job from the scheduler's default rather than being overruled by it");
        recorder.JobException.Should().BeNull();
    }

    /// <summary>
    /// A job that never looks at its token cannot be stopped by anything, and is reported as timed out
    /// when it finally returns.
    /// </summary>
    /// <remarks>
    /// The limit <c>JobInterruptMonitorPlugin</c> always had, kept honest here: cancellation is
    /// cooperative and nothing in .NET aborts code that declines to notice. What the middleware can still
    /// do is refuse to call the overrun a success.
    /// </remarks>
    [Test]
    public async Task AJobThatIgnoresItsTokenIsStillReportedAsTimedOut()
    {
        Recorder recorder = new(expectedFirings: 1);

        await RunScheduler(recorder, typeof(StubbornJob), quartz => quartz.AddJobTimeout(TimeSpan.FromMilliseconds(200)));

        recorder.Executions.Should().Be(1);
        recorder.JobException.Should().NotBeNull(
            "the job ran past its budget and returned as though nothing had happened; reporting that as a success "
            + "would hide every job whose cancellation handling is broken");
        recorder.JobException!.Message.Should().Contain("timed out");
    }

    /// <summary>
    /// The reason the timeout is raised as a <see cref="JobExecutionException" /> at all: it makes a
    /// timeout retryable, and the middleware runs again on the retry with the whole budget.
    /// </summary>
    [Test]
    public async Task ATimedOutFiringIsRetriedByTheTriggersRetryPolicy()
    {
        Recorder recorder = new(expectedFirings: 2);

        await RunScheduler(
            recorder,
            typeof(SlowThenQuickJob),
            quartz => quartz.AddJobTimeout(TimeSpan.FromMilliseconds(200)),
            trigger => trigger.WithRetryPolicy(RetryPolicy.Fixed(2, TimeSpan.FromMilliseconds(100))));

        recorder.Executions.Should().Be(2,
            "the timeout reached the trigger as a failure, so its retry policy scheduled another attempt - which "
            + "is the whole reason the middleware rethrows rather than letting the interrupt stand");
        recorder.RetryAttempts.Should().Equal([0, 1],
            "the retry is an ordinary re-acquisition with a new fire instance, so the pipeline runs again and the "
            + "second attempt is handed a fresh budget");
        recorder.JobException.Should().BeNull(
            "the second attempt finished inside its budget, so the occurrence ends successfully");
    }

    /// <summary>
    /// <c>tutorial/job-execution-middleware.md</c>: "Only the firing that overran is interrupted, because
    /// it is named by its fire instance id: two concurrent executions of one job are timed separately."
    /// That sentence is what makes <c>AddJobTimeout</c> safe on a job without
    /// <c>[DisallowConcurrentExecution]</c>, and every other test here runs a single firing, so none of
    /// them would notice an interrupt that reached for the job key instead.
    /// </summary>
    /// <remarks>
    /// The two firings are staggered rather than simultaneous, because two firings of one job type share
    /// one budget and only their start times can make the deadlines differ. The companion is still
    /// running when the earlier firing's budget runs out — it waits for exactly that — so what the
    /// assertions read is one firing being interrupted while another one of the same job is in flight.
    /// </remarks>
    [Test]
    public async Task OnlyTheFiringThatOverranIsInterrupted()
    {
        ConcurrentFiringRecorder recorder = new();

        await RunTwoStaggeredFirings(recorder, budget: TimeSpan.FromSeconds(3), companionDelay: TimeSpan.FromSeconds(1));

        recorder.Entered.Should().BeEquivalentTo([ConcurrentFiringRecorder.Overrunning, ConcurrentFiringRecorder.Companion],
            "both triggers fire the same job, and the job allows concurrent execution");
        recorder.Cancelled.Should().BeEquivalentTo([ConcurrentFiringRecorder.Overrunning],
            "the interrupt names a fire instance, so it reaches the firing that overran and no other execution of "
            + "the same job");
        recorder.OverlapObserved.Should().BeTrue(
            "the companion has to still be in flight when the other firing is interrupted, or this proves nothing "
            + "about two concurrent executions");

        recorder.Outcome(ConcurrentFiringRecorder.Overrunning).Should().NotBeNull()
            .And.Subject.As<JobExecutionException>().Message.Should()
            .Contain("timed out").And.Contain(recorder.FireInstanceOf(ConcurrentFiringRecorder.Overrunning),
                "the failure names the fire instance that spent the budget, which is what tells two concurrent "
                + "firings of one job apart in a log");

        recorder.Outcome(ConcurrentFiringRecorder.Companion).Should().BeNull(
            "the companion had a budget of its own, counted from its own fire, and finished inside it");
    }

    /// <summary>
    /// A scheduler-wide default of nothing is what leaving the argument out already says, so a
    /// non-positive one is a mistake worth refusing where it is written.
    /// </summary>
    [TestCase(0)]
    [TestCase(-1)]
    public void ANonPositiveSchedulerDefaultIsRefused(int seconds)
    {
        Action act = () => QuartzSchedulerBuilder.Create(q => q.AddJobTimeout(TimeSpan.FromSeconds(seconds)));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("defaultTimeout");
    }

    [Test]
    public void ABudgetThatIsNotATimeSpanIsRefusedWhereItIsWritten()
    {
        Action act = () => _ = new JobTimeoutAttribute("five minutes");

        act.Should().Throw<ArgumentException>().WithMessage("*TimeSpan*",
            "a value the attribute cannot parse must not quietly come to mean no timeout at all");
    }

    [Test]
    public void ANegativeBudgetIsRefused()
    {
        Action act = () => _ = new JobTimeoutAttribute("-00:00:01");

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }

    [Test]
    public void ABudgetOfZeroMeansNoTimeout()
    {
        new JobTimeoutAttribute("00:00:00").Timeout.Should().Be(TimeSpan.Zero,
            "zero is the spelling for a job that opts out of a scheduler-wide default");
    }

    /// <summary>
    /// Builds a scheduler with the given configuration, runs one occurrence of the job, and shuts
    /// everything down before returning — so an assertion never races the execution it is about.
    /// </summary>
    private static async Task RunScheduler(
        Recorder recorder,
        Type jobType,
        Action<IQuartzBuilder> configure,
        Action<ITriggerConfigurator<IJob>>? trigger = null)
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"job-{id}", $"group-{id}");

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = $"timeout-{id}";
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });
            quartz.AddJobListener(new CompletionListener(recorder));
            quartz.AddTriggerListener(new InstructionListener(recorder));
            configure(quartz);

            quartz.AddJob(jobType, job => job.WithIdentity(jobKey).StoreDurably());
            quartz.AddTrigger(configurator =>
            {
                configurator
                    .ForJob(jobKey)
                    .WithIdentity($"trigger-{id}")
                    // Exactly one scheduled occurrence, so anything that fires after it is a retry and
                    // nothing else.
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                    .StartNow();
                trigger?.Invoke(configurator);
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();
            await recorder.WaitForCompletion();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Runs two firings of one job over one scheduler: one that overruns the budget, and one that starts
    /// later and is still running when the first one's budget expires.
    /// </summary>
    private static async Task RunTwoStaggeredFirings(
        ConcurrentFiringRecorder recorder,
        TimeSpan budget,
        TimeSpan companionDelay)
    {
        string id = Guid.NewGuid().ToString("N");
        JobKey jobKey = new($"job-{id}", $"group-{id}");

        ServiceCollection services = new();
        services.AddSingleton(recorder);
        services.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options =>
            {
                options.InstanceName = $"timeout-concurrent-{id}";
                options.IdleWaitTime = TimeSpan.FromSeconds(1);
            });
            quartz.AddJobListener(new ConcurrentCompletionListener(recorder));
            quartz.AddJobTimeout(budget);

            quartz.AddJob<TwoSpeedJob>(job => job.WithIdentity(jobKey).StoreDurably());

            quartz.AddTrigger(configurator => configurator
                .ForJob(jobKey)
                .WithIdentity($"overrunning-{id}")
                .UsingJobData(ConcurrentFiringRecorder.RoleKey, ConcurrentFiringRecorder.Overrunning)
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                .StartNow());

            quartz.AddTrigger(configurator => configurator
                .ForJob(jobKey)
                .WithIdentity($"companion-{id}")
                .UsingJobData(ConcurrentFiringRecorder.RoleKey, ConcurrentFiringRecorder.Companion)
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                .StartAt(DateTimeOffset.UtcNow.Add(companionDelay)));
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler scheduler = provider.GetRequiredService<IScheduler>();
        try
        {
            await scheduler.Start();
            await recorder.WaitForCompletion();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// What the firings did, shared by the jobs and the listeners.
    /// </summary>
    public sealed class Recorder
    {
        private readonly Lock gate = new();
        private readonly List<int> retryAttempts = [];
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int expectedFirings;

        private int completions;

        public Recorder(int expectedFirings)
        {
            this.expectedFirings = expectedFirings;
        }

        public int Executions { get; private set; }

        public bool CancellationSeenByJob { get; private set; }

        public JobExecutionException? JobException { get; private set; }

        public SchedulerInstruction? Instruction { get; private set; }

        public IReadOnlyList<int> RetryAttempts
        {
            get
            {
                lock (gate)
                {
                    return [.. retryAttempts];
                }
            }
        }

        public void JobEntered(IJobExecutionContext context)
        {
            lock (gate)
            {
                Executions++;
                retryAttempts.Add(context.RetryAttempt);
            }
        }

        public void JobSawCancellation()
        {
            lock (gate)
            {
                CancellationSeenByJob = true;
            }
        }

        public void JobWasExecuted(JobExecutionException? jobException)
        {
            bool done;
            lock (gate)
            {
                JobException = jobException;
                done = ++completions >= expectedFirings;
            }

            if (done)
            {
                completed.TrySetResult();
            }
        }

        public void TriggerComplete(SchedulerInstruction instruction)
        {
            lock (gate)
            {
                Instruction = instruction;
            }
        }

        public Task WaitForCompletion() => completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// A job that waits until it is told to stop — and, if nothing tells it, gives up long before a test
    /// runner would, so that a middleware which fails to interrupt fails the test rather than hanging it.
    /// </summary>
    public class HangingJob : IJob
    {
        /// <summary>
        /// Long enough that no test's budget expires by accident, short enough that a shutdown waiting
        /// for jobs to complete always returns.
        /// </summary>
        private static readonly TimeSpan longEnoughToBeInterrupted = TimeSpan.FromSeconds(20);

        private readonly Recorder recorder;

        public HangingJob(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.JobEntered(context);
            try
            {
                await Task.Delay(longEnoughToBeInterrupted, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                recorder.JobSawCancellation();
                throw;
            }
        }
    }

    /// <summary>
    /// A job with a budget of its own, far shorter than any scheduler default a test sets, which runs
    /// longer than that budget and then finishes on its own.
    /// </summary>
    [JobTimeout("00:00:00.200")]
    public sealed class ImpatientJob : IJob
    {
        private readonly Recorder recorder;

        public ImpatientJob(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.JobEntered(context);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                recorder.JobSawCancellation();
                throw;
            }
        }
    }

    /// <summary>
    /// A job that says it has no timeout, and takes longer than the scheduler's default to prove it.
    /// </summary>
    [JobTimeout("00:00:00")]
    public sealed class UnboundedJob : IJob
    {
        private readonly Recorder recorder;

        public UnboundedJob(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.JobEntered(context);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                recorder.JobSawCancellation();
                throw;
            }
        }
    }

    public sealed class QuickJob : IJob
    {
        private readonly Recorder recorder;

        public QuickJob(Recorder recorder) => this.recorder = recorder;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.JobEntered(context);
            return default;
        }
    }

    /// <summary>
    /// A job that outstays its budget and never looks at its token.
    /// </summary>
    public sealed class StubbornJob : IJob
    {
        private readonly Recorder recorder;

        public StubbornJob(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.JobEntered(context);

            // Deliberately not forwarding the token, which is the thing CA2016 exists to flag.
#pragma warning disable CA2016
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
#pragma warning restore CA2016

            if (cancellationToken.IsCancellationRequested)
            {
                recorder.JobSawCancellation();
            }
        }
    }

    /// <summary>
    /// A job that hangs on its first attempt and returns at once on the next, so a retry can be told
    /// apart from a repeat.
    /// </summary>
    public sealed class SlowThenQuickJob : IJob
    {
        private readonly Recorder recorder;

        public SlowThenQuickJob(Recorder recorder) => this.recorder = recorder;

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.JobEntered(context);

            if (context.RetryAttempt > 0)
            {
                return;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                recorder.JobSawCancellation();
                throw;
            }
        }
    }

    /// <summary>
    /// What each of two concurrent firings of one job did, kept per firing rather than per job.
    /// </summary>
    public sealed class ConcurrentFiringRecorder
    {
        public const string RoleKey = "role";
        public const string Overrunning = "overrunning";
        public const string Companion = "companion";

        private readonly Lock gate = new();
        private readonly Dictionary<string, string> fireInstances = [];
        private readonly List<string> cancelled = [];
        private readonly Dictionary<string, JobExecutionException?> outcomes = [];
        private readonly TaskCompletionSource overrunInterrupted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Entered
        {
            get
            {
                lock (gate)
                {
                    return [.. fireInstances.Keys];
                }
            }
        }

        public IReadOnlyList<string> Cancelled
        {
            get
            {
                lock (gate)
                {
                    return [.. cancelled];
                }
            }
        }

        /// <summary>
        /// Whether the companion firing was in flight at the moment the overrunning one was reported.
        /// </summary>
        public bool OverlapObserved { get; private set; }

        public string FireInstanceOf(string role)
        {
            lock (gate)
            {
                return fireInstances[role];
            }
        }

        public JobExecutionException? Outcome(string role)
        {
            lock (gate)
            {
                return outcomes[role];
            }
        }

        public void Entering(string role, string fireInstanceId)
        {
            lock (gate)
            {
                fireInstances[role] = fireInstanceId;
            }
        }

        public void SawCancellation(string role)
        {
            lock (gate)
            {
                cancelled.Add(role);

                if (role == Overrunning)
                {
                    // Read where it is true or false for a reason: the companion is waiting on the
                    // signal below, so at this instant it has either entered and is in flight, or it
                    // has not started at all and the test is not about two concurrent firings.
                    OverlapObserved = fireInstances.ContainsKey(Companion) && !outcomes.ContainsKey(Companion);
                }
            }

            if (role == Overrunning)
            {
                overrunInterrupted.TrySetResult();
            }
        }

        public void JobWasExecuted(string role, JobExecutionException? jobException)
        {
            bool done;
            lock (gate)
            {
                outcomes[role] = jobException;
                done = outcomes.Count == 2;
            }

            if (done)
            {
                completed.TrySetResult();
            }
        }

        /// <summary>
        /// Held here so the companion firing stays in flight exactly until the other one is interrupted,
        /// and no longer — a wall-clock wait would either be racy or be most of the test's duration.
        /// </summary>
        public Task WaitForTheOverrunToBeInterrupted(CancellationToken cancellationToken)
        {
            return overrunInterrupted.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }

        public Task WaitForCompletion() => completed.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// One job, two roles: the firing that outstays the budget, and the one that starts later and waits
    /// for it to be interrupted before finishing.
    /// </summary>
    public sealed class TwoSpeedJob : IJob
    {
        private readonly ConcurrentFiringRecorder recorder;

        public TwoSpeedJob(ConcurrentFiringRecorder recorder) => this.recorder = recorder;

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            string role = context.MergedJobDataMap.GetString(ConcurrentFiringRecorder.RoleKey)!;
            recorder.Entering(role, context.FireInstanceId);

            try
            {
                if (role == ConcurrentFiringRecorder.Overrunning)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await recorder.WaitForTheOverrunToBeInterrupted(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                recorder.SawCancellation(role);
                throw;
            }
        }
    }

    private sealed class ConcurrentCompletionListener : IJobListener
    {
        private readonly ConcurrentFiringRecorder recorder;

        public ConcurrentCompletionListener(ConcurrentFiringRecorder recorder) => this.recorder = recorder;

        public string Name => "concurrent-completion";

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            recorder.JobWasExecuted(context.MergedJobDataMap.GetString(ConcurrentFiringRecorder.RoleKey)!, jobException);
            return default;
        }
    }

    private sealed class CompletionListener : IJobListener
    {
        private readonly Recorder recorder;

        public CompletionListener(Recorder recorder) => this.recorder = recorder;

        public string Name => "completion";

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
        {
            recorder.JobWasExecuted(jobException);
            return default;
        }
    }

    private sealed class InstructionListener : ITriggerListener
    {
        private readonly Recorder recorder;

        public InstructionListener(Recorder recorder) => this.recorder = recorder;

        public string Name => "instruction";

        public ValueTask TriggerComplete(
            ITrigger trigger,
            IJobExecutionContext context,
            SchedulerInstruction triggerInstructionCode,
            CancellationToken cancellationToken = default)
        {
            recorder.TriggerComplete(triggerInstructionCode);
            return default;
        }
    }
}
