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

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What <c>[DisallowConcurrentExecution]</c> and <c>[PersistJobDataAfterExecution]</c> mean for a job
/// type, and how often the question is asked.
/// </summary>
/// <remarks>
/// The attribute walk used to be <c>ObjectUtils.IsAnyInterfaceAttributePresent</c>, with a companion
/// <c>IsAttributePresent</c> that nothing outside the class called (#3432). It is private to this type
/// now — <c>JobDetailImpl</c>, <c>JobBuilder</c> and the ADO store's acquisition loop all arrive
/// through <see cref="JobTypeInformation.GetOrCreate" />, so that is where it is tested from.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class JobTypeInformationTest
{
    [Test]
    public void AnAttributeOnABaseClassCounts()
    {
        JobTypeInformation.GetOrCreate(typeof(ExtendedJob)).ConcurrentExecutionDisallowed
            .Should().BeTrue("the attribute is on the base class, and the lookup asks with inherit: true");

        JobTypeInformation.GetOrCreate(typeof(ExtendedJob)).PersistJobDataAfterExecution
            .Should().BeFalse("nothing in that hierarchy carries the other one");

        JobTypeInformation.GetOrCreate(typeof(ReallyExtendedJob)).PersistJobDataAfterExecution
            .Should().BeTrue("a derived class may add what its base did not say");
    }

    /// <summary>
    /// The interfaces are walked flat rather than recursively, which is only right because
    /// <see cref="Type.GetInterfaces" /> already reports the ones an interface itself inherits. This is
    /// what says so — the attribute here is two hops away.
    /// </summary>
    [Test]
    public void AnAttributeOnAnInheritedInterfaceCounts()
    {
        JobTypeInformation.GetOrCreate(typeof(DerivedContractJob)).ConcurrentExecutionDisallowed
            .Should().BeTrue("the attribute is on an interface the job's own interface inherits");

        JobTypeInformation.GetOrCreate(typeof(DerivedContractJob)).PersistJobDataAfterExecution
            .Should().BeFalse("nothing in the hierarchy carries that one");

        typeof(DerivedContractJob).GetCustomAttributes(typeof(DisallowConcurrentExecutionAttribute), inherit: true)
            .Should().BeEmpty("the class hierarchy alone does not carry it, so only the interface walk can have produced that answer");
    }

    /// <summary>
    /// <c>[JobTimeout]</c> travels the same walk as the two boolean attributes, which is what
    /// <c>tutorial/job-execution-middleware.md</c> promises: "the attribute is inherited from a base
    /// class or from an interface the job implements, so a contract can set the budget for everything
    /// that fulfils it".
    /// </summary>
    [Test]
    public void ATimeoutOnABaseClassOrAnInterfaceCounts()
    {
        JobTypeInformation.GetOrCreate(typeof(ExtendedTimedJob)).Timeout
            .Should().Be(TimeSpan.FromSeconds(30), "the budget is on the base class, and the lookup asks with inherit: true");

        JobTypeInformation.GetOrCreate(typeof(DerivedTimedContractJob)).Timeout
            .Should().Be(TimeSpan.FromSeconds(10), "a contract can set the budget for everything that fulfils it");

        typeof(DerivedTimedContractJob).GetCustomAttributes(typeof(JobTimeoutAttribute), inherit: true)
            .Should().BeEmpty("the class hierarchy alone does not carry it, so only the interface walk can have produced that answer");

        JobTypeInformation.GetOrCreate(typeof(ExtendedJob)).Timeout
            .Should().BeNull("nothing in that hierarchy states a budget, so the scheduler's own default applies");
    }

    /// <summary>
    /// The attribute carries a value rather than merely being present, so unlike the two boolean ones
    /// it can disagree with itself along the walk. A job that states its own budget is not overruled by
    /// a base class or by a contract it happens to fulfil — including when what it states is
    /// <c>"00:00:00"</c>, which is how a long-running job exempts itself.
    /// </summary>
    [Test]
    public void ATypesOwnTimeoutBeatsAnInheritedOne()
    {
        JobTypeInformation.GetOrCreate(typeof(RetimedJob)).Timeout
            .Should().Be(TimeSpan.FromMinutes(1), "the derived class states its own budget, and the base's is not it");

        JobTypeInformation.GetOrCreate(typeof(UntimedContractJob)).Timeout
            .Should().Be(TimeSpan.Zero,
                "a zero budget means no timeout at all, and a job that declares one has to be able to say so "
                + "over a contract that bounds everything else fulfilling it");
    }

    [Test]
    public void TheAnswerIsRememberedPerType()
    {
        JobTypeInformation.GetOrCreate(typeof(BaseJob))
            .Should().BeSameAs(JobTypeInformation.GetOrCreate(typeof(BaseJob)),
                "trigger acquisition asks once per trigger, so the attribute lookup is memoized rather than repeated");
    }

    [DisallowConcurrentExecution]
    private interface INonConcurrentContract : IJob;

    private interface IDerivedContract : INonConcurrentContract;

    private sealed class DerivedContractJob : IDerivedContract
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    private class BaseJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private class ExtendedJob : BaseJob;

    [PersistJobDataAfterExecution]
    private sealed class ReallyExtendedJob : ExtendedJob;

    [JobTimeout("00:00:30")]
    private class TimedBaseJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ExtendedTimedJob : TimedBaseJob;

    [JobTimeout("00:01:00")]
    private sealed class RetimedJob : TimedBaseJob;

    [JobTimeout("00:00:10")]
    private interface ITimedContract : IJob;

    private interface IDerivedTimedContract : ITimedContract;

    private sealed class DerivedTimedContractJob : IDerivedTimedContract
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [JobTimeout("00:00:00")]
    private sealed class UntimedContractJob : ITimedContract
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
