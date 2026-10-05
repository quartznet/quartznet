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

using System.Globalization;
using System.Reflection;

using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <summary>
/// What <see cref="RetryPolicyAttribute" /> declares, what it refuses, and where a refusal is reported.
/// </summary>
[TestFixture]
public sealed class RetryPolicyAttributeTest
{
    [Test]
    public void EachConstructorIsTheFactoryOfTheSameShape()
    {
        new RetryPolicyAttribute(3, "00:05:00").Policy.Should().Be(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));
        new RetryPolicyAttribute(5, "00:00:30", 2).Policy.Should().Be(RetryPolicy.Exponential(5, TimeSpan.FromSeconds(30), 2),
            "three arguments are the exponential factory's first three");
        new RetryPolicyAttribute("00:00:10", "00:01:00", "01:00:00").Policy.Should().Be(
            RetryPolicy.Explicit(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromHours(1)));
        new RetryPolicyAttribute(0).Policy.Should().BeSameAs(RetryPolicy.None,
            "[RetryPolicy(0)] is how a job type says it is never retried, whatever the scheduler's default");
    }

    [Test]
    public void TheExponentialFormTakesTheFactorysOtherTwoArgumentsByName()
    {
        RetryPolicyAttribute attribute = new(5, "00:00:30", 2) { MaxDelay = "00:10:00", Jitter = 0.2 };

        attribute.Policy.Should().Be(RetryPolicy.Exponential(5, TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(10), 0.2));
        attribute.MaxDelay.Should().Be("00:10:00");
        attribute.Jitter.Should().Be(0.2);
        attribute.MaxAttempts.Should().Be(5);
    }

    [Test]
    public void MaxAttemptsIsWhatThePolicyRetries()
    {
        new RetryPolicyAttribute(0).MaxAttempts.Should().Be(0);
        new RetryPolicyAttribute(3, "00:00:01").MaxAttempts.Should().Be(3);
        new RetryPolicyAttribute("00:00:01", "00:00:02").MaxAttempts.Should().Be(2, "an explicit policy retries once per delay");
    }

    [Test]
    public void DelaysAreReadInvariantly()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            // fi-FI writes a decimal comma, so a culture-sensitive parse would read the same source as a
            // different wait on a Finnish build agent than on any other.
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");

            new RetryPolicyAttribute(2, "00:00:01.5").Policy.Should().Be(RetryPolicy.Fixed(2, TimeSpan.FromMilliseconds(1500)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Test]
    public void ACountWithNoDelayIsRefusedUnlessItIsZero()
    {
        Action act = () => _ = new RetryPolicyAttribute(3);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxAttempts")
            .WithMessage("*[RetryPolicy(3, \"00:01:00\")]*[RetryPolicy(0)]*",
                "the message shows both things the author could have meant");
    }

    [Test]
    public void ZeroAttemptsWithADelayPointsAtTheNoRetryForm()
    {
        Action fixedForm = () => _ = new RetryPolicyAttribute(0, "00:01:00");
        Action exponentialForm = () => _ = new RetryPolicyAttribute(0, "00:01:00", 2);

        fixedForm.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*[RetryPolicy(0)]*");
        exponentialForm.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*[RetryPolicy(0)]*");
    }

    [TestCase("five minutes")]
    [TestCase("")]
    public void ADelayThatIsNotATimeSpanIsRefused(string delay)
    {
        Action act = () => _ = new RetryPolicyAttribute(3, delay);

        act.Should().Throw<ArgumentException>().WithParameterName("delay").WithMessage($"*'{delay}' is not a TimeSpan*");
    }

    [Test]
    public void WhatTheFactoryRefusesTheAttributeRefuses()
    {
        Action negativeCount = () => _ = new RetryPolicyAttribute(-1, "00:00:01");
        Action negativeDelay = () => _ = new RetryPolicyAttribute(3, "-00:00:01");
        Action shrinkingBackoff = () => _ = new RetryPolicyAttribute(3, "00:00:01", 0.5);
        Action ceilingBelowTheFirstWait = () => _ = new RetryPolicyAttribute(3, "00:01:00", 2) { MaxDelay = "00:00:10" };
        Action jitterPastTheBand = () => _ = new RetryPolicyAttribute(3, "00:01:00", 2) { Jitter = 1.5 };
        Action noDelays = () => _ = new RetryPolicyAttribute();
        Action nullDelays = () => _ = new RetryPolicyAttribute((string[]) null);
        Action aDelayThatIsNot = () => _ = new RetryPolicyAttribute("00:00:01", "soon");

        negativeCount.Should().Throw<ArgumentOutOfRangeException>();
        negativeDelay.Should().Throw<ArgumentOutOfRangeException>();
        shrinkingBackoff.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("factor");
        ceilingBelowTheFirstWait.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxDelay");
        jitterPastTheBand.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("jitter");
        noDelays.Should().Throw<ArgumentException>().WithMessage("*at least one delay*");
        nullDelays.Should().Throw<ArgumentNullException>();
        aDelayThatIsNot.Should().Throw<ArgumentException>().WithParameterName("delays");
    }

    [Test]
    public void ACeilingOrAJitterOnAnythingButTheExponentialFormIsRefused()
    {
        Action ceilingOnFixed = () => _ = new RetryPolicyAttribute(3, "00:01:00") { MaxDelay = "00:10:00" };
        Action jitterOnExplicit = () => _ = new RetryPolicyAttribute("00:00:01") { Jitter = 0.1 };
        Action jitterOnNone = () => _ = new RetryPolicyAttribute(0) { Jitter = 0.1 };

        ceilingOnFixed.Should().Throw<ArgumentException>().WithParameterName("MaxDelay").WithMessage("*exponential*",
            "a fixed policy has no ceiling to take, and silently dropping one would hide the author's mistake");
        jitterOnExplicit.Should().Throw<ArgumentException>().WithParameterName("Jitter");
        jitterOnNone.Should().Throw<ArgumentException>().WithParameterName("Jitter");
    }

    [Test]
    public void TheJobTypesAttributeIsReadOffTheTypeItsBaseOrItsContract()
    {
        JobTypeInformation.GetOrCreate(typeof(FixedJob)).RetryPolicy.Should().Be(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)));
        JobTypeInformation.GetOrCreate(typeof(ExponentialJob)).RetryPolicy.Should().Be(
            RetryPolicy.Exponential(5, TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(10), 0.2));
        JobTypeInformation.GetOrCreate(typeof(ExplicitJob)).RetryPolicy.Should().Be(
            RetryPolicy.Explicit(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1)));
        JobTypeInformation.GetOrCreate(typeof(NeverRetriedJob)).RetryPolicy.Should().BeSameAs(RetryPolicy.None);

        JobTypeInformation.GetOrCreate(typeof(DerivedFromFixedJob)).RetryPolicy.Should().Be(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5)),
            "the attribute is inherited from a base class, as [JobTimeout] is");
        JobTypeInformation.GetOrCreate(typeof(ContractJob)).RetryPolicy.Should().Be(RetryPolicy.Fixed(1, TimeSpan.FromSeconds(1)),
            "a contract can declare the policy for everything that fulfils it");
        JobTypeInformation.GetOrCreate(typeof(ContractJobWithItsOwn)).RetryPolicy.Should().BeSameAs(RetryPolicy.None,
            "the type's own declaration wins over its contract's, including when it says never");
        JobTypeInformation.GetOrCreate(typeof(UndeclaredJob)).RetryPolicy.Should().BeNull(
            "a type that says nothing leaves the scheduler's default in charge");
    }

    [Test]
    public void AnAttributeThatIsNotAPolicyCostsTheTypeItsPolicyAndNothingElse()
    {
        JobTypeInformation information = JobTypeInformation.GetOrCreate(typeof(BadDelayJob));

        information.RetryPolicy.Should().BeNull();
        information.RetryPolicyError.Should().BeOfType<ArgumentException>().Which.Message.Should().Contain("'soon' is not a TimeSpan");
        information.ConcurrentExecutionDisallowed.Should().BeTrue(
            "what the type says about concurrency is still needed to fire it, so one bad attribute must not take the rest with it");
    }

    [Test]
    public void ARefusedNamedArgumentIsReportedAsWhatItThrewNotAsAMissingProperty()
    {
        // The runtime reports an exception from a named argument's setter as a CustomAttributeFormatException
        // saying the property "was not found", with the real reason two InnerExceptions down.
        JobTypeInformation.GetOrCreate(typeof(CeilingOnFixedJob)).RetryPolicyError
            .Should().BeOfType<ArgumentException>()
            .Which.Message.Should().Contain("exponential");
    }

    [Test]
    public void EveryWayReadingTheAttributeCanFailStaysInsideTheTypeInformation()
    {
        JobTypeInformation.IsUnreadableAttribute(new ArgumentOutOfRangeException("delay")).Should().BeTrue(
            "a constructor's own exception arrives as it was thrown");
        JobTypeInformation.IsUnreadableAttribute(new CustomAttributeFormatException("not found", new TargetInvocationException(new ArgumentException("x"))))
            .Should().BeTrue("a named argument's setter arrives wrapped twice");
        JobTypeInformation.IsUnreadableAttribute(new TargetInvocationException(new ArgumentException("x"))).Should().BeTrue(
            "a runtime that invokes the constructor by reflection may wrap it, and every firing of the type asks for this information, "
            + "so nothing the attribute says may escape it");
        JobTypeInformation.IsUnreadableAttribute(new InvalidOperationException("x")).Should().BeFalse(
            "something that is not about the attribute's arguments is not the attribute's to swallow");
    }

    [Test]
    public async Task AddingAJobWhoseAttributeIsNotAPolicyFailsNamingTheType()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder.Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "retry-attribute-" + Guid.NewGuid().ToString("N"))
                .UseInMemoryStore())
            .BuildScheduler();

        try
        {
            IJobDetail job = JobBuilder.Create<BadDelayJob>().WithIdentity("bad", "retry-attribute").StoreDurably().Build();
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("bad", "retry-attribute").ForJob(job).StartNow().Build();

            Func<Task> add = async () => await scheduler.AddJob(job);
            Func<Task> schedule = async () => await scheduler.ScheduleJob(job, trigger);
            Func<Task> scheduleMany = async () => await scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [job] = [trigger] });

            foreach (Func<Task> act in new[] { add, schedule, scheduleMany })
            {
                (await act.Should().ThrowAsync<SchedulerException>()
                        .WithMessage($"*{typeof(BadDelayJob).FullName}*[RetryPolicy]*'soon' is not a TimeSpan*",
                            "an attribute that is not a policy is reported where the job is added, rather than discovered when it first fails"))
                    .Which.InnerException.Should().BeOfType<ArgumentException>();
            }

            (await scheduler.Exists(job.Key)).Should().BeFalse("a refused job is not stored");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task AJobWhoseTypeCannotBeResolvedHereIsNotRefusedForIt()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder.Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "retry-attribute-" + Guid.NewGuid().ToString("N"))
                .UseInMemoryStore())
            .BuildScheduler();

        try
        {
            // A node of a heterogeneous cluster may add a job whose assembly only the nodes that run it have.
            IJobDetail job = JobBuilder.Create()
                .OfType((JobType) "Somewhere.Else.ImportJob, Somewhere.Else")
                .WithIdentity("elsewhere", "retry-attribute")
                .StoreDurably()
                .Build();

            await scheduler.AddJob(job);

            (await scheduler.Exists(job.Key)).Should().BeTrue();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [RetryPolicy(3, "00:05:00")]
    private class FixedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class DerivedFromFixedJob : FixedJob;

    [RetryPolicy(5, "00:00:30", 2, MaxDelay = "00:10:00", Jitter = 0.2)]
    private sealed class ExponentialJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy("00:00:10", "00:01:00")]
    private sealed class ExplicitJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy(0)]
    private sealed class NeverRetriedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy(1, "00:00:01")]
    private interface IRetriedContract : IJob;

    private sealed class ContractJob : IRetriedContract
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy(0)]
    private sealed class ContractJobWithItsOwn : IRetriedContract
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class UndeclaredJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    [RetryPolicy(3, "soon")]
    private sealed class BadDelayJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [RetryPolicy(3, "00:01:00", MaxDelay = "00:10:00")]
    private sealed class CeilingOnFixedJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
