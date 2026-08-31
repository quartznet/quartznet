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

    /// <summary>
    /// Rescheduling stores a trigger, so it normalizes one.
    /// </summary>
    /// <remarks>
    /// <c>RescheduleJob</c> was the one call that stored a trigger without preparing its data map, so an
    /// input placed on the replacement reached the store as whatever object the caller built. The
    /// in-memory store hands the very object back and the job could not tell; a store that serializes
    /// could, which is the asymmetry <see cref="TheStoredInputIsAString" /> exists to rule out.
    /// </remarks>
    [Test]
    public async Task TheInputOnARescheduledTriggerIsStoredAsAString()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(TheInputOnARescheduledTriggerIsStoredAsAString), start: false);

        TriggerKey key = new("later", "typed");

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>().WithIdentity("email", "typed").Build(),
            TriggerBuilder.Create().WithIdentity(key).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());

        await harness.Scheduler.RescheduleJob(key, TriggerBuilder.Create<SendEmailJob>()
            .WithIdentity(key)
            .StartAt(DateTimeOffset.UtcNow.AddHours(2))
            .UsingInput(new SendEmail("rescheduled@example.org", "after the move"))
            .Build());

        ITrigger stored = await harness.Scheduler.GetTrigger(key);

        stored.JobDataMap[SchedulerConstants.JobInput].Should().BeOfType<string>(
            "a replacement trigger reaches a store by the same road as an original one, so its input has "
            + "to be the string every store path can carry")
            .Which.Should().Be("""{"To":"rescheduled@example.org","Subject":"after the move"}""");
    }

    [Test]
    public async Task AnInputOnARescheduledTriggerReachesTheJob()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(AnInputOnARescheduledTriggerReachesTheJob));

        TriggerKey key = new("later", "typed");

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>().WithIdentity("email", "typed").Build(),
            TriggerBuilder.Create().WithIdentity(key).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());

        await harness.Scheduler.RescheduleJob(key, TriggerBuilder.Create<SendEmailJob>()
            .WithIdentity(key)
            .StartNow()
            .UsingInput(new SendEmail("rescheduled@example.org", "after the move"))
            .Build());

        object received = await recorder.Received;

        received.Should().Be(new SendEmail("rescheduled@example.org", "after the move"),
            "the firing a reschedule produces reads its input the same way any other firing does");
    }

    /// <summary>
    /// Updating a trigger's data map is the third way a map reaches a store.
    /// </summary>
    /// <remarks>
    /// It is not a scheduling call — fire times and trigger state are preserved — so it deliberately
    /// does not touch the trace-context keys. The input still has to be normalized, because that is
    /// about what a store can hold rather than about what scheduled anything.
    /// </remarks>
    [Test]
    public async Task TheInputOnAnUpdatedTriggerIsStoredAsAString()
    {
        Recorder recorder = new();
        await using SchedulerHarness harness = await SchedulerHarness.Start(recorder, nameof(TheInputOnAnUpdatedTriggerIsStoredAsAString), start: false);

        TriggerKey key = new("later", "typed");

        await harness.Scheduler.ScheduleJob(
            JobBuilder.Create<SendEmailJob>().WithIdentity("email", "typed").Build(),
            TriggerBuilder.Create().WithIdentity(key).StartAt(DateTimeOffset.UtcNow.AddHours(1)).Build());

        JobDataMap replacement = new();
        replacement[SchedulerConstants.JobInput] = new SendEmail("updated@example.org", "edited in place");

        (await harness.Scheduler.UpdateTriggerDetails(key, new TriggerDetailsUpdate().WithJobDataMap(replacement)))
            .Should().BeTrue("the trigger exists, so the update applies");

        ITrigger stored = await harness.Scheduler.GetTrigger(key);

        stored.JobDataMap[SchedulerConstants.JobInput].Should().BeOfType<string>(
            "UpdateTriggerDetails replaces the whole map, so it is a way into the store like any other")
            .Which.Should().Be("""{"To":"updated@example.org","Subject":"edited in place"}""");
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
    public void TryGetInputAnswersFalseWhenNothingWasScheduled()
    {
        IJobExecutionContext context = new JobExecutionContextImpl(
            scheduler: null,
            TestUtil.NewMinimalTriggerFiredBundle(),
            job: null,
            new SystemTextJsonJobInputSerializer());

        context.TryGetInput(out SendEmail input).Should().BeFalse(
            "a firing stored before the job took a typed input carries nothing under the key, and an application upgrading from 3.x has to be able to ask rather than be thrown at");

        input.Should().BeNull("nothing was read, so nothing is handed back");
    }

    [Test]
    public void TryGetInputReadsTheStoredPayload()
    {
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle();
        bundle.Trigger.JobDataMap[SchedulerConstants.JobInput] = """{"To":"someone@example.org","Subject":"hello"}""";

        IJobExecutionContext context = new JobExecutionContextImpl(
            scheduler: null,
            bundle,
            job: null,
            new SystemTextJsonJobInputSerializer());

        context.TryGetInput(out SendEmail input).Should().BeTrue("the firing carries an input, which is the whole of what the answer means");

        input.Should().Be(new SendEmail("someone@example.org", "hello"),
            "asking whether there is one and reading it are the same operation, so a caller does not deserialize twice");
    }

    [Test]
    public void TryGetInputStillThrowsOnAValueItCannotRead()
    {
        TriggerFiredBundle bundle = TestUtil.NewMinimalTriggerFiredBundle();
        bundle.Trigger.JobDataMap[SchedulerConstants.JobInput] = "not json at all";

        IJobExecutionContext context = new JobExecutionContextImpl(
            scheduler: null,
            bundle,
            job: null,
            new SystemTextJsonJobInputSerializer());

        Action act = () => context.TryGetInput(out SendEmail _);

        act.Should().Throw<SchedulerException>()
            .WithMessage("*not json at all*",
                "corruption is not compatibility: only an absent input is an answer, and a present one that will not read is still a failure");
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
