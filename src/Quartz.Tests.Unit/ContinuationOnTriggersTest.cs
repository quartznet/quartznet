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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Serialization.Newtonsoft;

namespace Quartz.Tests.Unit;

/// <summary>
/// How a continuation sits on a trigger: what the builders carry, what a rebuild keeps, and what the
/// blob shape has to hold for a trigger to come back still waiting for the right firing.
/// </summary>
[TestFixture]
public class ContinuationOnTriggersTest
{
    private static readonly TriggerKey parent = new TriggerKey("import", "nightly");

    [Test]
    public void ATriggerWaitsForNothingUnlessItIsToldTo()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .Build();

        trigger.Continuation.Should().Be(Continuation.None);
        trigger.Continuation.IsNone.Should().BeTrue();
    }

    [Test]
    public void StartAfterPutsTheParentAndTheConditionOnTheTrigger()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .StartAfter(parent, ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation)
            .Build();

        trigger.Continuation.Parent.Should().Be(parent);
        trigger.Continuation.When.Should().Be(ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation);
        trigger.Continuation.IsNone.Should().BeFalse();
    }

    [Test]
    public void StartAfterDefaultsToWaitingForSuccess()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .StartAfter(parent)
            .Build();

        trigger.Continuation.When.Should().Be(ContinuationCondition.OnSuccess,
            "'run this after that one' means 'after that one worked' unless the caller says otherwise");
    }

    /// <summary>
    /// <c>StartAfter</c> composes with a schedule rather than replacing one, which is what "start this
    /// cron once the import has finished" needs.
    /// </summary>
    [Test]
    public void AContinuationKeepsWhateverScheduleItWasGiven()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .StartAfter(parent)
            .WithCronSchedule("0 0 3 * * ?")
            .Build();

        trigger.Should().BeOfType<CronTriggerImpl>();
        trigger.Continuation.Parent.Should().Be(parent);
    }

    [Test]
    public void AContinuationSurvivesBeingRebuiltFromItsTrigger()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .StartAfter(parent, ContinuationCondition.OnAnyOutcome)
            .Build();

        ITrigger rebuilt = trigger.GetTriggerBuilder().Build();

        rebuilt.Continuation.Should().Be(trigger.Continuation,
            "rebuilding a trigger is how a reschedule keeps everything the caller did not change, and what "
            + "it waits for is part of its definition");
    }

    [Test]
    public void ARebuiltTriggerThatWaitedForNothingStillWaitsForNothing()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .Build();

        trigger.GetTriggerBuilder().Build().Continuation.Should().Be(Continuation.None);
    }

    [Test]
    public void AConditionThatNamesNoOutcomeIsRefused()
    {
        Action naming0 = () => Continuation.After(parent, 0);

        naming0.Should().Throw<ArgumentOutOfRangeException>(
            "a condition nothing satisfies would discard the continuation whatever the parent did, which is a "
            + "schedule nobody means to write");

        Action namingSomethingElse = () => Continuation.After(parent, (ContinuationCondition) 64);

        namingSomethingElse.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ContinuationsAreEqualWhenTheyWaitForTheSameThing()
    {
        Continuation.After(parent).Should().Be(Continuation.After(parent, ContinuationCondition.OnSuccess));
        Continuation.After(parent).Should().NotBe(Continuation.After(parent, ContinuationCondition.OnFailure));
        Continuation.After(parent).Should().NotBe(Continuation.After(new TriggerKey("other", "nightly")));
    }

    /// <summary>
    /// The System.Text.Json path, which is what a 4.x <c>BLOB_TRIGGERS</c> blob goes through.
    /// </summary>
    [Test]
    public void AContinuationSurvivesASystemTextJsonRoundTrip()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .StartAfter(parent, ContinuationCondition.OnVeto)
            .Build();

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();

        IOperableTrigger restored = serializer.Deserialize<IOperableTrigger>(serializer.Serialize(trigger))!;

        restored.Continuation.Should().Be(trigger.Continuation);
    }

    [Test]
    public void ATriggerWaitingForNothingSurvivesASystemTextJsonRoundTrip()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .Build();

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();

        IOperableTrigger restored = serializer.Deserialize<IOperableTrigger>(serializer.Serialize(trigger))!;

        restored.Continuation.Should().Be(Continuation.None,
            "a null parent name is no continuation, not an empty one");
    }

    [Test]
    public void AContinuationSurvivesANewtonsoftRoundTrip()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .StartAfter(parent, ContinuationCondition.OnFailure)
            .Build();

        NewtonsoftJsonObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();

        IOperableTrigger restored = serializer.Deserialize<IOperableTrigger>(serializer.Serialize(trigger))!;

        restored.Continuation.Should().Be(trigger.Continuation);
    }

    [Test]
    public void ATriggerWaitingForNothingSurvivesANewtonsoftRoundTrip()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .Build();

        NewtonsoftJsonObjectSerializer serializer = new NewtonsoftJsonObjectSerializer();

        IOperableTrigger restored = serializer.Deserialize<IOperableTrigger>(serializer.Serialize(trigger))!;

        restored.Continuation.Should().Be(Continuation.None);
    }

    /// <summary>
    /// A blob written by 4.1, which has none of the three fields.
    /// </summary>
    [Test]
    public void ATriggerFromAnOlderBlobWaitsForNothing()
    {
        TriggerBase trigger = (TriggerBase) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SimpleTriggerImpl));

        trigger.Continuation.Should().Be(Continuation.None,
            "an instance no constructor ran for has all three fields at their defaults, and that has to read "
            + "as a trigger that waits for nothing rather than as one nothing can release");
    }

    /// <summary>
    /// A row that names a parent but whose condition column is null — written by a node that knew the
    /// columns but not the value, or repaired by hand.
    /// </summary>
    [Test]
    public void AStoredContinuationWithNoConditionWaitsForAnyOutcome()
    {
        Continuation restored = Continuation.FromStored("import", "nightly", condition: null);

        restored.Parent.Should().Be(parent);
        restored.When.Should().Be(ContinuationCondition.OnAnyOutcome,
            "the alternative is a continuation nothing can release, which is a job that silently never runs");
    }

    /// <summary>
    /// An <see cref="ITriggerConfigurator{TJob}" /> of somebody else's, written before 4.2 and so
    /// implementing none of what 4.2 added: the default body is what runs.
    /// </summary>
    [Test]
    public void AConfiguratorThatDoesNotBuildContinuationsSaysSo()
    {
        ITriggerConfigurator<IJob> configurator = A.Fake<ITriggerConfigurator<IJob>>();
        A.CallTo(() => configurator.StartAfter(A<TriggerKey>._, A<ContinuationCondition>._)).CallsBaseMethod();

        Action startAfter = () => configurator.StartAfter(parent);

        startAfter.Should().Throw<NotSupportedException>(
            "the member is a default interface member so that an implementation written against 4.0 keeps "
            + "compiling — and it says so rather than quietly building a trigger that waits for nothing");
    }

    /// <summary>
    /// The other side of the same promise: an <see cref="IJobStore" /> written before 4.2 is handed
    /// the completion through the member it already implements, minus the outcome it cannot use.
    /// </summary>
    [Test]
    public async Task AStoreThatDoesNotSettleContinuationsIsStillToldTheFiringIsOver()
    {
        IJobStore store = A.Fake<IJobStore>();
        A.CallTo(() => store.FiringComplete(A<TriggeredJobCompleteContext>._, A<CancellationToken>._)).CallsBaseMethod();

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .Build();

        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("j", "jg").Build();

        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = trigger,
            JobDetail = job,
            Instruction = SchedulerInstruction.SetTriggerComplete,
            Outcome = ExecutionOutcome.Succeeded
        });

        A.CallTo(() => store.TriggeredJobComplete(trigger, job, SchedulerInstruction.SetTriggerComplete, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
