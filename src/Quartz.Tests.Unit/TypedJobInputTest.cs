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

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// A job that declares the type of its input gets it as a parameter, whichever end of the schedule put
/// it there.
/// </summary>
[NonParallelizable]
public sealed class TypedJobInputTest
{
    public sealed record SendEmail(string To, string Subject);

    public readonly record struct Reminder(int Id, string Note);

    [Test]
    public async Task AnInputOnTheJobReachesTheJob()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(AnInputOnTheJobReachesTheJob));

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>()
                .WithIdentity("email", "typed")
                .UsingInput(new SendEmail("someone@example.org", "hello"))
                .Build(),
            TriggerBuilder.Create().WithIdentity("now", "typed").StartNow().Build());

        object received = await recorder.Received;

        received.Should().Be(new SendEmail("someone@example.org", "hello"),
            "an IJob<TInput> is handed the payload the job was built with, deserialized back to its own type");
    }

    [Test]
    public async Task ATriggerInputOverridesTheJobInput()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(ATriggerInputOverridesTheJobInput));

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>()
                .WithIdentity("email", "typed")
                .UsingInput(new SendEmail("job@example.org", "from the job"))
                .Build(),
            TriggerBuilder.Create<SendEmailJob>()
                .WithIdentity("now", "typed")
                .StartNow()
                .UsingInput(new SendEmail("trigger@example.org", "from the trigger"))
                .Build());

        object received = await recorder.Received;

        received.Should().Be(new SendEmail("trigger@example.org", "from the trigger"),
            "the input follows MergedJobDataMap precedence, so the trigger's value wins over the job's");
    }

    [Test]
    public async Task ARecordStructPayloadRoundTrips()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(ARecordStructPayloadRoundTrips));

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<ReminderJob>().WithIdentity("reminder", "typed").Build(),
            TriggerBuilder.Create<ReminderJob>()
                .WithIdentity("now", "typed")
                .StartNow()
                .UsingInput(new Reminder(42, "stand up"))
                .Build());

        object received = await recorder.Received;

        received.Should().Be(new Reminder(42, "stand up"),
            "a value-type payload is serialized and read back like any other, not boxed straight through");
    }

    [Test]
    public async Task TheStoredInputIsAString()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(TheStoredInputIsAString), start: false);

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>()
                .WithIdentity("email", "typed")
                .UsingInput(new SendEmail("someone@example.org", "hello"))
                .Build(),
            TriggerBuilder.Create().WithIdentity("later", "typed").StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());

        IJobDetail stored = await harness.Scheduler.GetJobDetail(new JobKey("email", "typed"));

        stored.JobDataMap[SchedulerConstants.JobInput].Should().BeOfType<string>(
            "the value has to survive StoreJobDataAsStrings, the JSON write gate, the blob path and the wire, and a string survives all of them")
            .Which.Should().Be("""{"To":"someone@example.org","Subject":"hello"}""");
    }

    [Test]
    public async Task GetInputReadsTheInputFromAnUntypedJob()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(GetInputReadsTheInputFromAnUntypedJob));

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<UntypedReadingJob>().WithIdentity("untyped", "typed").Build(),
            TriggerBuilder.Create()
                .WithIdentity("now", "typed")
                .StartNow()
                .UsingJobData(SchedulerConstants.JobInput, new SendEmail("plain@example.org", "read by hand"))
                .Build());

        object received = await recorder.Received;

        received.Should().Be(new SendEmail("plain@example.org", "read by hand"),
            "GetInput<T>() is an extension on the context, so a plain IJob can read a typed input too");
    }

    [Test]
    public void GetInputAnswersDefaultWhenNothingWasScheduled()
    {
        IJobExecutionContext context = new JobExecutionContextImpl(
            scheduler: null,
            TestUtil.NewMinimalTriggerFiredBundle(),
            job: null,
            new SystemTextJsonJobInputSerializer());

        context.GetInput<SendEmail>().Should().BeNull(
            "reading an input that was never set is a question with an answer, unlike an IJob<TInput> whose input is missing");
    }

    [Test]
    public async Task AMissingInputFailsTheFiringByName()
    {
        SendEmailJob job = new(new Recorder());
        IJobExecutionContext context = new JobExecutionContextImpl(
            scheduler: null,
            TestUtil.NewMinimalTriggerFiredBundle(),
            job,
            new SystemTextJsonJobInputSerializer());

        Func<Task> act = async () => await ((IJob) job).Execute(context);

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage($"*{SchedulerConstants.JobInput}*",
                "a typed job whose input is missing has to say which key it looked under, not run with a default payload");
    }

    [Test]
    public async Task AContextBuiltWithoutASerializerSaysSo()
    {
        SendEmailJob job = new(new Recorder());
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle();
        bundle.Trigger.JobDataMap[SchedulerConstants.JobInput] = """{"To":"someone@example.org","Subject":"hello"}""";

        IJobExecutionContext context = new JobExecutionContextImpl(scheduler: null, bundle, job);

        Func<Task> act = async () => await ((IJob) job).Execute(context);

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("*IJobInputSerializer*",
                "a hand-built context names what it is missing rather than reflecting its way to an answer");
    }

    [Test]
    public async Task APersistedJobWritesTheInputBackUnchanged()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(APersistedJobWritesTheInputBackUnchanged));

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<PersistingSendEmailJob>()
                .WithIdentity("persisting", "typed")
                // Durable, so the completed one-shot trigger does not take the job — and the map this
                // test is about — with it.
                .StoreDurably()
                .UsingInput(new SendEmail("someone@example.org", "hello"))
                .Build(),
            TriggerBuilder.Create().WithIdentity("now", "typed").StartNow().Build());

        await recorder.Received;

        // The write-back happens in the same store operation that retires the completed one-shot trigger,
        // so a trigger that is gone is a job whose data map has already been re-stored.
        await harness.WaitUntilTriggerIsGone(new TriggerKey("now", "typed"));

        IJobDetail stored = await harness.Scheduler.GetJobDetail(new JobKey("persisting", "typed"));

        stored.JobDataMap[SchedulerConstants.JobInput].Should().Be("""{"To":"someone@example.org","Subject":"hello"}""",
            "a [PersistJobDataAfterExecution] job re-stores its own map, and the input in it is already the string the reader expects");
    }

    /// <summary>
    /// A scheduler over the in-memory store with a recorder in its container, so the job the factory
    /// builds can report what it was handed.
    /// </summary>
    private sealed class SchedulerHarness : IAsyncDisposable
    {
        private readonly ServiceProvider container;

        private SchedulerHarness(ServiceProvider container, IScheduler scheduler)
        {
            this.container = container;
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public static async Task<SchedulerHarness> Start(Recorder recorder, string name, bool start = true)
        {
            ServiceCollection services = new();
            services.AddSingleton(recorder);
            services.AddQuartz(q => q.ConfigureScheduler(options =>
            {
                options.InstanceName = name;
                options.InstanceId = "one";
            }));

            ServiceProvider container = services.BuildServiceProvider();
            IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

            if (start)
            {
                await scheduler.Start();
            }

            return new SchedulerHarness(container, scheduler);
        }

        public async Task WaitUntilTriggerIsGone(TriggerKey key)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (await Scheduler.GetTrigger(key) is not null)
            {
                DateTimeOffset.UtcNow.Should().BeBefore(deadline, $"trigger '{key}' should have completed and been removed");
                await Task.Delay(20);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.Shutdown(waitForJobsToComplete: false);
            await container.DisposeAsync();
        }
    }

    /// <summary>
    /// Carries what a firing was handed back to the test. Registered in the container, so the job factory
    /// hands the running job the very instance the test is waiting on.
    /// </summary>
    public sealed class Recorder
    {
        private readonly TaskCompletionSource<object> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<object> Received => completion.Task.WaitAsync(TimeSpan.FromSeconds(30));

        public void Record(object value) => completion.TrySetResult(value);
    }

    public sealed class SendEmailJob : IJob<SendEmail>
    {
        private readonly Recorder recorder;

        public SendEmailJob(Recorder recorder) => this.recorder = recorder;

        public ValueTask Execute(IJobExecutionContext context, SendEmail input, CancellationToken cancellationToken = default)
        {
            recorder.Record(input);
            return default;
        }
    }

    [PersistJobDataAfterExecution]
    public sealed class PersistingSendEmailJob : IJob<SendEmail>
    {
        private readonly Recorder recorder;

        public PersistingSendEmailJob(Recorder recorder) => this.recorder = recorder;

        public ValueTask Execute(IJobExecutionContext context, SendEmail input, CancellationToken cancellationToken = default)
        {
            recorder.Record(input);
            return default;
        }
    }

    public sealed class ReminderJob : IJob<Reminder>
    {
        private readonly Recorder recorder;

        public ReminderJob(Recorder recorder) => this.recorder = recorder;

        public ValueTask Execute(IJobExecutionContext context, Reminder input, CancellationToken cancellationToken = default)
        {
            recorder.Record(input);
            return default;
        }
    }

    public sealed class UntypedReadingJob : IJob
    {
        private readonly Recorder recorder;

        public UntypedReadingJob(Recorder recorder) => this.recorder = recorder;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            recorder.Record(context.GetInput<SendEmail>());
            return default;
        }
    }
}
