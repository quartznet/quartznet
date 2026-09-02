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

using Quartz.Extensibility;
using Quartz.Impl.Calendar;
using Quartz.Jobs;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// What <see cref="IScheduler" /> does with an argument it cannot work with.
/// </summary>
/// <remarks>
/// <para>
/// Until 4.0.0-beta.1 a null argument raised <see cref="SchedulerException" /> — Java parity, and
/// invisible: nothing on the interface said so, no test pinned it, and the
/// <see cref="SchedulerQueryExtensions" /> methods sitting next door on the same type already used
/// <see cref="ArgumentNullException" />. A caller writing <c>catch (ArgumentNullException)</c> caught
/// nothing, and one writing <c>catch (SchedulerException)</c> around a scheduling call was catching
/// its own bug along with the scheduler's failures.
/// </para>
/// <para>
/// The theory below is the contract: every member — reading as well as mutating — every reference
/// argument it will not accept as null, and the parameter named in the exception. beta.1 brought the
/// mutation members under it and left the reads as they were, which is two contracts on one interface
/// with nothing to tell a caller which member is which. It runs against
/// <see cref="Quartz.Impl.RAMJobStore" />, because refusing an argument happens before any store is
/// asked anything.
/// </para>
/// </remarks>
public sealed class SchedulerArgumentContractTest
{
    private ServiceProvider container = null!;
    private IScheduler scheduler = null!;

    [OneTimeSetUp]
    public async Task StartScheduler()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = nameof(SchedulerArgumentContractTest)));

        container = services.BuildServiceProvider();
        scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    [OneTimeTearDown]
    public async Task StopScheduler()
    {
        await scheduler.Shutdown();
        await container.DisposeAsync();
    }

    private static IJobDetail Job => JobBuilder.Create<NoOpJob>().WithIdentity("job", "group").StoreDurably().Build();

    private static ITrigger Trigger => TriggerBuilder.Create().WithIdentity("trigger", "group").ForJob(Job).Build();

    public static IEnumerable<TestCaseData> NullArguments()
    {
        yield return Case("jobDetail", s => s.ScheduleJob(null!, Trigger).AsTask());
        yield return Case("trigger", s => s.ScheduleJob(Job, (ITrigger) null!).AsTask());
        yield return Case("trigger", s => s.ScheduleJob((ITrigger) null!).AsTask());
        yield return Case("jobDetail", s => s.ScheduleJob(null!, new[] { Trigger }).AsTask());
        yield return Case("triggersForJob", s => s.ScheduleJob(Job, (IReadOnlyCollection<ITrigger>) null!).AsTask());
        yield return Case("triggersAndJobs", s => s.ScheduleJobs(null!).AsTask());
        yield return Case("jobDetail", s => s.AddJob(null!).AsTask());
        yield return Case("triggerKey", s => s.UnscheduleJob(null!).AsTask());
        yield return Case("triggerKeys", s => s.UnscheduleJobs((IReadOnlyCollection<TriggerKey>) null!).AsTask());
        yield return Case("matcher", s => s.UnscheduleJobs((GroupMatcher<TriggerKey>) null!).AsTask());
        yield return Case("triggerKey", s => s.RescheduleJob(null!, Trigger).AsTask());
        yield return Case("newTrigger", s => s.RescheduleJob(new TriggerKey("trigger", "group"), null!).AsTask());
        yield return Case("triggerKey", s => s.UpdateTriggerDetails(null!, new TriggerDetailsUpdate()).AsTask());
        yield return Case("update", s => s.UpdateTriggerDetails(new TriggerKey("trigger", "group"), null!).AsTask());
        yield return Case("jobKey", s => s.DeleteJob(null!).AsTask());
        yield return Case("jobKeys", s => s.DeleteJobs((IReadOnlyCollection<JobKey>) null!).AsTask());
        yield return Case("matcher", s => s.DeleteJobs((GroupMatcher<JobKey>) null!).AsTask());
        yield return Case("jobKey", s => s.TriggerJob(null!).AsTask());
        yield return Case("jobKey", s => s.PauseJob(null!).AsTask());
        yield return Case("jobKeys", s => s.PauseJobs((IReadOnlyCollection<JobKey>) null!).AsTask());
        yield return Case("matcher", s => s.PauseJobGroups(null!).AsTask());
        yield return Case("triggerKey", s => s.PauseTrigger(null!).AsTask());
        yield return Case("triggerKeys", s => s.PauseTriggers((IReadOnlyCollection<TriggerKey>) null!).AsTask());
        yield return Case("matcher", s => s.PauseTriggerGroups(null!).AsTask());
        yield return Case("jobKey", s => s.ResumeJob(null!).AsTask());
        yield return Case("jobKeys", s => s.ResumeJobs((IReadOnlyCollection<JobKey>) null!).AsTask());
        yield return Case("matcher", s => s.ResumeJobGroups(null!).AsTask());
        yield return Case("triggerKey", s => s.ResumeTrigger(null!).AsTask());
        yield return Case("triggerKeys", s => s.ResumeTriggers((IReadOnlyCollection<TriggerKey>) null!).AsTask());
        yield return Case("matcher", s => s.ResumeTriggerGroups(null!).AsTask());
        yield return Case("triggerKey", s => s.ResetTriggerFromErrorState(null!).AsTask());
        yield return Case("triggerKeys", s => s.ResetTriggersFromErrorState((IReadOnlyCollection<TriggerKey>) null!).AsTask());
        yield return Case("matcher", s => s.ResetTriggersFromErrorState((GroupMatcher<TriggerKey>) null!).AsTask());
        yield return Case("calendarName", s => s.AddCalendar(null!, new HolidayCalendar()).AsTask());
        yield return Case("calendar", s => s.AddCalendar("calendar", null!).AsTask());
        yield return Case("calendarName", s => s.DeleteCalendar(null!).AsTask());
        yield return Case("jobKey", s => s.Interrupt(null!).AsTask());
        yield return Case("fireInstanceId", s => s.InterruptFireInstance(null!).AsTask());

        // The read members, which beta.1 left alone: a scheduler that refuses a null on the way in and
        // dereferences one on the way out is two contracts, and a caller has no way to know which
        // member is which.
        yield return Case("jobKey", s => s.GetJobDetail(null!).AsTask());
        yield return Case("jobKeys", s => s.GetJobDetails(null!).AsTask());
        yield return Case("triggerKey", s => s.GetTrigger(null!).AsTask());
        yield return Case("triggerKeys", s => s.GetTriggers(null!).AsTask());
        yield return Case("triggerKey", s => s.GetTriggerState(null!).AsTask());
        yield return Case("jobKey", s => s.GetTriggersOfJob(null!).AsTask());
        yield return Case("calendarName", s => s.GetCalendar(null!).AsTask());
        yield return Case("jobKey", s => s.Exists((JobKey) null!).AsTask());
        yield return Case("triggerKey", s => s.Exists((TriggerKey) null!).AsTask());
        yield return Case("calendarName", s => s.Exists((string) null!).AsTask());
        yield return Case("query", s => s.QueryJobs(null!).AsTask());
        yield return Case("query", s => s.QueryTriggers(null!).AsTask());
        yield return Case("query", s => s.QueryJobGroups(null!).AsTask());
        yield return Case("query", s => s.QueryTriggerGroups(null!).AsTask());
        yield return Case("query", s => s.QueryCalendarNames(null!).AsTask());
        yield return Case("query", s => s.QueryFireInstances(null!).AsTask());

        static TestCaseData Case(string parameter, Func<IScheduler, Task> call)
        {
            return new TestCaseData(parameter, call).SetArgDisplayNames(parameter);
        }
    }

    [TestCaseSource(nameof(NullArguments))]
    public async Task ASchedulerMemberRefusesANullArgumentByName(string parameter, Func<IScheduler, Task> call)
    {
        Func<Task> act = async () => await call(scheduler);

        ArgumentNullException failure = (await act.Should().ThrowAsync<ArgumentNullException>(
            "a null argument is the caller's mistake, and the type that says so is the one every .NET "
            + "caller already catches — not SchedulerException, which is what the scheduler raises when "
            + "the scheduling itself will not work")).Which;

        failure.ParamName.Should().Be(parameter,
            "the exception has to say which argument, or a call with four of them tells the caller nothing");
    }

    /// <summary>
    /// The other refusal worth pinning, because it is the one that enforces the read-model rule
    /// <see cref="ITrigger" />'s own remarks describe: the scheduler and the stores operate on
    /// <see cref="IOperableTrigger" />, and an object implementing only the read model is not a trigger
    /// anybody can schedule.
    /// </summary>
    [Test]
    public async Task ATriggerThatIsOnlyTheReadModelIsRefusedWithTheFix()
    {
        Func<Task> act = async () => await scheduler.ScheduleJob(Job, new ReadModelTrigger());

        (await act.Should().ThrowAsync<SchedulerException>(
            "this is a scheduling refusal rather than an argument that is missing, so it is the "
            + "scheduler's own exception type"))
            .WithMessage("*ReadModelTrigger*", "the type that cannot be scheduled is named")
            .WithMessage("*TriggerBuilder*", "and so is what to use instead")
            .WithMessage("*TriggerBase*", "for the case where a trigger type of one's own really is wanted");
    }

    /// <summary>
    /// An <see cref="ITrigger" /> and nothing more — the shape a test double or a mapping layer
    /// produces when somebody implements the interface the API hands them.
    /// </summary>
    private sealed class ReadModelTrigger : ITrigger
    {
        public TriggerKey Key { get; } = new("read-model", "group");
        public JobKey JobKey { get; } = new("job", "group");
        public string? Description => null;
        public string? CalendarName => null;
        public JobDataMap JobDataMap { get; } = new();
        public DateTimeOffset? FinalFireTimeUtc => null;
        public int MisfireInstructionCode => 0;
        public int Priority => 5;
        public DateTimeOffset StartTimeUtc => DateTimeOffset.UtcNow;
        public DateTimeOffset? EndTimeUtc => null;
        public DateTimeOffset? NextFireTimeUtc => DateTimeOffset.UtcNow;
        public DateTimeOffset? PreviousFireTimeUtc => null;
        public string? ExecutionGroup => null;
        public PreferredNode PreferredNode => PreferredNode.None;
        public RetryPolicy? RetryPolicy => null;
        public int RetryAttempt => 0;
        public string FireInstanceId => "";
        public TriggerFamily Family => TriggerFamily.Simple;

        public bool MayFireAgain => true;

        public DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime) => afterTime;
        public IScheduleBuilder GetScheduleBuilder() => throw new NotSupportedException();
        public TriggerBuilder<IJob> GetTriggerBuilder() => throw new NotSupportedException();
        public ITrigger Clone() => this;
        public int CompareTo(ITrigger? other) => 0;
    }
}
