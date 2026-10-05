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

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// Which policy a failed firing is retried under — the trigger's own, the job type's
/// <see cref="RetryPolicyAttribute" />, or the scheduler's default — decided with no scheduler running.
/// </summary>
/// <remarks>
/// <see cref="RetryPolicyInheritanceExecutionTest" /> is the same precedence through a running scheduler
/// on both stores; this is the decision itself.
/// </remarks>
[TestFixture]
public sealed class RetryPolicyInheritanceTest
{
    private static readonly DateTimeOffset now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly RetryPolicy triggers = RetryPolicy.Fixed(1, TimeSpan.FromMinutes(1));
    private static readonly RetryPolicy jobTypes = RetryPolicy.Fixed(2, TimeSpan.FromMinutes(2));
    private static readonly RetryPolicy schedulers = RetryPolicy.Fixed(3, TimeSpan.FromMinutes(3));

    public static IEnumerable<TestCaseData> Precedence()
    {
        yield return new TestCaseData(null, typeof(PlainJob), null, null).SetName("nothing anywhere is no retry");
        yield return new TestCaseData(null, typeof(PlainJob), schedulers, schedulers).SetName("the default covers a trigger and a job type that say nothing");
        yield return new TestCaseData(null, typeof(DeclaringJob), schedulers, jobTypes).SetName("the job type beats the default");
        yield return new TestCaseData(null, typeof(DeclaringJob), null, jobTypes).SetName("the job type applies with no default");
        yield return new TestCaseData(triggers, typeof(DeclaringJob), schedulers, triggers).SetName("the trigger beats the job type and the default");
        yield return new TestCaseData(triggers, typeof(PlainJob), schedulers, triggers).SetName("the trigger beats the default");
        yield return new TestCaseData(RetryPolicy.None, typeof(DeclaringJob), schedulers, null).SetName("a trigger's None refuses the job type and the default");
        yield return new TestCaseData(null, typeof(NeverRetriedJob), schedulers, null).SetName("a job type's None refuses the default");
        yield return new TestCaseData(triggers, typeof(NeverRetriedJob), schedulers, triggers).SetName("a trigger's own policy beats a job type's None");
        yield return new TestCaseData(null, typeof(PlainJob), RetryPolicy.None, null).SetName("a default of None is no default");
    }

    [TestCaseSource(nameof(Precedence))]
    public void TheFirstPolicyFoundDecides(RetryPolicy triggerPolicy, Type jobType, RetryPolicy schedulerDefault, RetryPolicy expected)
    {
        IJobDetail job = JobBuilder.Create().OfType(jobType).WithIdentity("job", "retries").Build();

        RetryPolicyResolution.Effective(triggerPolicy, job, schedulerDefault).Should().Be(expected);
    }

    [Test]
    public void TheContextAnswersWithTheJobTypesPolicyWhenTheTriggerHasNone()
    {
        JobExecutionContextImpl context = Context(typeof(DeclaringJob), triggerPolicy: null);

        context.RetryPolicy.Should().Be(jobTypes);
        context.Trigger.RetryPolicy.Should().BeNull("the job type's policy is looked up, never written onto the trigger");
    }

    [Test]
    public void TheContextNeverAnswersNone()
    {
        Context(typeof(DeclaringJob), RetryPolicy.None).RetryPolicy.Should().BeNull(
            "None means no retry, so a reader comparing RetryAttempt with MaxAttempts never sees a policy of zero attempts");
        Context(typeof(NeverRetriedJob), triggerPolicy: null).RetryPolicy.Should().BeNull();
    }

    [Test]
    public void AContextOverASchedulerQuartzDidNotBuildHasNoDefault()
    {
        Context(typeof(PlainJob), triggerPolicy: null).RetryPolicy.Should().BeNull(
            "the default belongs to the scheduler that fired the trigger, and a hand-built context names none");
    }

    [Test]
    public void ATriggerWithNoPolicyIsRetriedUnderItsJobTypes()
    {
        JobExecutionContextImpl context = Context(typeof(DeclaringJob), triggerPolicy: null);
        IOperableTrigger trigger = (IOperableTrigger) context.Trigger;

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        trigger.RetryAttempt.Should().Be(1);
        trigger.NextFireTimeUtc.Should().Be(now + jobTypes.InitialDelay, "the wait is the job type's policy's");
        trigger.RetryPolicy.Should().BeNull("the trigger's column stays its own; nothing is back-filled into it");
    }

    [Test]
    public void TheJobTypesAttemptsAreTheCeiling()
    {
        JobExecutionContextImpl context = Context(typeof(DeclaringJob), triggerPolicy: null, retryAttempt: 2);
        IOperableTrigger trigger = (IOperableTrigger) context.Trigger;

        trigger.ExecutionComplete(context, Failure()).Should().NotBe(SchedulerInstruction.RetryTrigger,
            "two retries are what the job type's policy allows, and both are spent");
        trigger.RetryAttempt.Should().Be(0, "the occurrence is settled, as it is when a trigger's own policy runs out");
    }

    [Test]
    public void ATriggersNoneIsNotRetriedWhateverTheJobTypeSays()
    {
        JobExecutionContextImpl context = Context(typeof(DeclaringJob), RetryPolicy.None);
        IOperableTrigger trigger = (IOperableTrigger) context.Trigger;

        trigger.ExecutionComplete(context, Failure()).Should().NotBe(SchedulerInstruction.RetryTrigger);
        trigger.RetryAttempt.Should().Be(0);
        trigger.RetryPolicy.Should().BeSameAs(RetryPolicy.None, "the opt-out is the trigger's, and it stays on the trigger");
    }

    [Test]
    public void ATriggersOwnPolicyIsDecidedWithoutAskingTheContext()
    {
        // A context that would answer with something else: the trigger's own policy is not the context's
        // to overrule, so a context of somebody else's cannot change what a policy-carrying trigger does.
        IOperableTrigger trigger = Trigger(triggers);
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.RetryPolicy).Returns(schedulers);

        trigger.ExecutionComplete(context, Failure()).Should().Be(SchedulerInstruction.RetryTrigger);
        trigger.NextFireTimeUtc.Should().Be(now + triggers.InitialDelay);
        A.CallTo(() => context.RetryPolicy).MustNotHaveHappened();
    }

    [Test]
    public void ASuccessDoesNotLookForAPolicy()
    {
        IOperableTrigger trigger = Trigger(triggerPolicy: null);
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();

        trigger.ExecutionComplete(context, result: null);

        // Only a failure needs a policy, so a firing that succeeds pays nothing for the lookup.
        A.CallTo(() => context.RetryPolicy).MustNotHaveHappened();
    }

    [Test]
    public void TheDefaultImplementationAnswersWithTheTriggersOwnPolicy()
    {
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.RetryPolicy).CallsBaseMethod();

        A.CallTo(() => context.Trigger).Returns(Trigger(triggers));
        context.RetryPolicy.Should().Be(triggers);

        A.CallTo(() => context.Trigger).Returns(Trigger(RetryPolicy.None));
        context.RetryPolicy.Should().BeNull("None is never a policy a firing is retried under");

        A.CallTo(() => context.Trigger).Returns(Trigger(triggerPolicy: null));
        context.RetryPolicy.Should().BeNull("a context implemented outside Quartz knows no job type's policy and no default");
    }

    [Test]
    public void TheDefaultIsEachSchedulersOwnAndTheLastCallWins()
    {
        RetryPolicy first = RetryPolicy.Fixed(1, TimeSpan.FromSeconds(1));
        RetryPolicy second = RetryPolicy.Fixed(2, TimeSpan.FromSeconds(2));
        RetryPolicy named = RetryPolicy.Fixed(3, TimeSpan.FromSeconds(3));

        ServiceCollection services = new();
        services.AddQuartz(q => q.UseDefaultRetryPolicy(first).UseDefaultRetryPolicy(second));
        services.AddQuartz("acme", q => q.UseDefaultRetryPolicy(named));
        services.AddQuartz("plain", _ => { });

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptionsMonitor<SchedulerRetryOptions> options = provider.GetRequiredService<IOptionsMonitor<SchedulerRetryOptions>>();

        options.Get(Options.DefaultName).DefaultPolicy.Should().Be(second, "a second call replaces the default rather than adding to it");
        options.Get("acme").DefaultPolicy.Should().Be(named, "each scheduler has its own default");
        options.Get("plain").DefaultPolicy.Should().BeNull("a scheduler that set no default has none");
    }

    [Test]
    public void ADefaultIsAPolicy()
    {
        Action act = () => new ServiceCollection().AddQuartz(q => q.UseDefaultRetryPolicy(null));

        act.Should().Throw<ArgumentNullException>().WithParameterName("policy",
            "leaving the call out is how a scheduler has no default; RetryPolicy.None is how it says so out loud");
    }

    private static JobExecutionException Failure() => new(new InvalidOperationException("boom"));

    /// <summary>A one-shot trigger, just fired, so a retry has nothing to land beside.</summary>
    private static IOperableTrigger Trigger(RetryPolicy triggerPolicy, int retryAttempt = 0)
    {
        FakeTimeProvider clock = new(now);
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity("one-shot", "retries")
            .ForJob("job", "retries")
            .StartAt(now)
            .WithRetryPolicy(triggerPolicy)
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        trigger.Triggered(null);
        trigger.RetryAttempt = retryAttempt;
        return trigger;
    }

    private static JobExecutionContextImpl Context(Type jobType, RetryPolicy triggerPolicy, int retryAttempt = 0)
    {
        IOperableTrigger trigger = Trigger(triggerPolicy, retryAttempt);
        TriggerFiredBundle bundle = new()
        {
            JobDetail = JobBuilder.Create().OfType(jobType).WithIdentity("job", "retries").Build(),
            Trigger = trigger,
            Recovering = false,
            FireTimeUtc = now,
            ScheduledFireTimeUtc = now,
            PreviousFireTimeUtc = null,
            NextFireTimeUtc = null,
        };

        return new JobExecutionContextImpl(null, bundle, null);
    }

    private sealed class PlainJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy(2, "00:02:00")]
    private sealed class DeclaringJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy(0)]
    private sealed class NeverRetriedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
