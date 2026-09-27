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

using System.Text;
using System.Text.Json;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Triggers;
using Quartz.Util;

namespace Quartz.Tests.Unit;

/// <summary>
/// How an overlap policy sits on a trigger: what the builders carry, what a rebuild keeps, what every
/// shape a trigger travels in writes, and what the default interface members answer.
/// </summary>
[TestFixture]
public class OverlapPolicyOnTriggersTest
{
    [Test]
    public void ATriggerHasTheDefaultPolicyUnlessItIsGivenOne()
    {
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();

        trigger.OverlapPolicy.Should().Be(OverlapPolicy.Default,
            "a trigger nobody gave a policy behaves as every trigger did before policies existed");
    }

    [TestCase(OverlapPolicy.Skip)]
    [TestCase(OverlapPolicy.BufferOne)]
    [TestCase(OverlapPolicy.CancelPrevious)]
    [TestCase(OverlapPolicy.AllowAll)]
    public void ThePolicyIsCarriedByTheBuilderAndKeptByARebuild(OverlapPolicy policy)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithCronSchedule("0 0 3 * * ?")
            .WithOverlapPolicy(policy)
            .Build();

        trigger.OverlapPolicy.Should().Be(policy);
        trigger.GetTriggerBuilder().Build().OverlapPolicy.Should().Be(policy,
            "rebuilding a trigger is how a reschedule keeps what the caller did not change");
    }

    [Test]
    public void AValueThatIsNotAPolicyIsRefusedWhereverItIsGiven()
    {
        const OverlapPolicy notAPolicy = (OverlapPolicy) 42;

        Action builder = () => TriggerBuilder.Create().WithOverlapPolicy(notAPolicy);
        Action property = () => new SimpleTriggerImpl { OverlapPolicy = notAPolicy };
        Action update = () => new TriggerDetailsUpdate().WithOverlapPolicy(notAPolicy);

        builder.Should().Throw<ArgumentOutOfRangeException>("the column stores the integer, and 42 means nothing");
        property.Should().Throw<ArgumentOutOfRangeException>();
        update.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ATriggerOutsideTheBuiltInHierarchyCannotBeGivenAPolicyByTheBuilder()
    {
        IMutableTrigger custom = A.Fake<IMutableTrigger>();
        IScheduleBuilder schedule = A.Fake<IScheduleBuilder>();
        A.CallTo(() => schedule.Build()).Returns(custom);

        Action withPolicy = () => TriggerBuilder.Create().WithSchedule(schedule).WithOverlapPolicy(OverlapPolicy.Skip).Build();
        Action withoutOne = () => TriggerBuilder.Create().WithSchedule(schedule).Build();

        withPolicy.Should().Throw<NotSupportedException>(
            "a policy the trigger cannot carry would be dropped in silence, which is the one outcome that must not happen")
            .WithMessage("*TriggerBase*Skip*");
        withoutOne.Should().NotThrow("Default is what such a trigger already has");
    }

    [Test]
    public void TheInterfaceDefaultsAnswerWithoutAnImplementation()
    {
        ITrigger trigger = A.Fake<ITrigger>();
        A.CallTo(() => trigger.OverlapPolicy).CallsBaseMethod();
        trigger.OverlapPolicy.Should().Be(OverlapPolicy.Default,
            "a trigger written against 4.2 has no policy, which is Default");

        ITriggerConfigurator<IJob> configurator = A.Fake<ITriggerConfigurator<IJob>>();
        A.CallTo(() => configurator.WithOverlapPolicy(A<OverlapPolicy>._)).CallsBaseMethod();
        Action configure = () => configurator.WithOverlapPolicy(OverlapPolicy.Skip);
        configure.Should().Throw<NotSupportedException>(
            "a configurator that predates the member says so rather than building a trigger that overlaps");
    }

    [Test]
    public async Task TheListenerAndSignalerDefaultsDoNothing()
    {
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();

        ITriggerListener listener = A.Fake<ITriggerListener>();
        A.CallTo(() => listener.TriggerSkipped(A<ITrigger>._, A<IScheduler>._, A<CancellationToken>._)).CallsBaseMethod();
        await listener.TriggerSkipped(trigger, A.Fake<IScheduler>());

        ISchedulerSignaler signaler = A.Fake<ISchedulerSignaler>();
        A.CallTo(() => signaler.NotifyTriggerListenersSkipped(A<ITrigger>._, A<CancellationToken>._)).CallsBaseMethod();
        await signaler.NotifyTriggerListenersSkipped(trigger);

        A.CallTo(() => signaler.NotifyTriggerListenersMisfired(A<ITrigger>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [TestCase(StoredTriggerState.Executing, true)]
    [TestCase(StoredTriggerState.Acquired, false)]
    public async Task TheDelegateDefaultAsksTheTriggersFiredRows(StoredTriggerState state, bool running)
    {
        TriggerKey key = new("t", "g");
        IDriverDelegate driverDelegate = A.Fake<IDriverDelegate>();
        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(A<ConnectionAndTransactionHolder>._, A<FiredTriggerQuery>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<FiredTriggerRecord>>(
            [
                new FiredTriggerRecord
                {
                    FireInstanceId = "f1",
                    FireInstanceState = state,
                    TriggerKey = key,
                    SchedulerInstanceId = "node"
                }
            ]));
        A.CallTo(() => driverDelegate.IsTriggerCurrentlyExecuting(A<ConnectionAndTransactionHolder>._, A<TriggerKey>._, A<CancellationToken>._))
            .CallsBaseMethod();

        (await driverDelegate.IsTriggerCurrentlyExecuting(null!, key)).Should().Be(running,
            "a delegate written against 4.2 answers from the rows it already reads: only an executing one is running");

        A.CallTo(() => driverDelegate.SelectFiredTriggerRecords(A<ConnectionAndTransactionHolder>._, A<FiredTriggerQuery>.That.Matches(q => key.Equals(q.Trigger)), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public void ADeclinedFiringCarriesNothingToRunAndNoFailure()
    {
        TriggerFiredResult declined = TriggerFiredResult.Declined;

        declined.IsDeclined.Should().BeTrue();
        declined.TriggerFiredBundle.Should().BeNull();
        declined.Exception.Should().BeNull();
        TriggerFiredResult.NotFired.IsDeclined.Should().BeFalse(
            "a trigger that was not firable is released by the scheduler; a declined one was settled by the store");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The shapes a trigger travels in
    //////////////////////////////////////////////////////////////////////////////////////////////

    [TestCase(OverlapPolicy.Skip)]
    [TestCase(OverlapPolicy.CancelPrevious)]
    public void ThePolicySurvivesTheSystemTextJsonRoundTripByName(OverlapPolicy policy)
    {
        SystemTextJsonObjectSerializer serializer = new();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g").ForJob("j", "jg").WithOverlapPolicy(policy).Build();

        byte[] payload = serializer.Serialize(trigger);

        Encoding.UTF8.GetString(payload).Should().Contain($"\"OverlapPolicy\":\"{policy}\"",
            "the policy is written by name, which a reader of the payload can read");
        serializer.Deserialize<IOperableTrigger>(payload)!.OverlapPolicy.Should().Be(policy);
    }

    [Test]
    public void ADefaultTriggersSystemTextJsonPayloadIsTheOneItAlwaysWrote()
    {
        SystemTextJsonObjectSerializer serializer = new();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();

        Encoding.UTF8.GetString(serializer.Serialize(trigger)).Should().NotContain("OverlapPolicy",
            "a trigger with no policy is written exactly as a 4.2 node wrote it");
    }

    [TestCase("\"CancelPrevious\"", OverlapPolicy.CancelPrevious)]
    [TestCase("\"bufferone\"", OverlapPolicy.BufferOne)]
    [TestCase("1", OverlapPolicy.Skip)]
    [TestCase("\"Sometimes\"", OverlapPolicy.Default)]
    [TestCase("99", OverlapPolicy.Default)]
    [TestCase("null", OverlapPolicy.Default)]
    public void TheSystemTextJsonReaderIsForgiving(string written, OverlapPolicy expected)
    {
        SystemTextJsonObjectSerializer serializer = new();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();

        string payload = Encoding.UTF8.GetString(serializer.Serialize(trigger));
        payload = payload[..^1] + $",\"OverlapPolicy\":{written}}}";

        serializer.Deserialize<IOperableTrigger>(Encoding.UTF8.GetBytes(payload))!.OverlapPolicy.Should().Be(expected,
            "a payload a newer node wrote still reads as a schedule, as an unreadable retry policy reads as none");
    }

    [TestCase(null, OverlapPolicy.Default)]
    [TestCase("skip", OverlapPolicy.Skip)]
    [TestCase(" BufferOne ", OverlapPolicy.BufferOne)]
    [TestCase("CANCELPREVIOUS", OverlapPolicy.CancelPrevious)]
    [TestCase("AllowAll", OverlapPolicy.AllowAll)]
    [TestCase("Default", OverlapPolicy.Default)]
    public void AFileNamesAPolicyInAnyCase(string? written, OverlapPolicy expected)
    {
        SchedulingFileValues.ReadOverlapPolicy(written, "Trigger 't'").Should().Be(expected);
    }

    [TestCase("3")]
    [TestCase("Sometimes")]
    public void AFileNamingNoPolicyIsRefused(string written)
    {
        Action read = () => SchedulingFileValues.ReadOverlapPolicy(written, "Trigger 't'");

        read.Should().Throw<SchedulerConfigException>(
                "the integer the column holds is not a second spelling, and a misspelling names the choices")
            .WithMessage($"Trigger 't': '{written}' is not an overlap policy. Name one of*");
    }

    [TestCase(null, OverlapPolicy.Default, false)]
    [TestCase(1, OverlapPolicy.Skip, false)]
    [TestCase(3, OverlapPolicy.CancelPrevious, false)]
    [TestCase(19, OverlapPolicy.CancelPrevious, true)]
    [TestCase(17, OverlapPolicy.Skip, false)]
    [TestCase(99, OverlapPolicy.Default, false)]
    public void TheColumnSpellsThePolicyAndTheStoresOwnMark(int? stored, OverlapPolicy policy, bool unsettled)
    {
        OverlapPolicyColumn.Decode(stored).Should().Be((policy, unsettled),
            "only CancelPrevious is ever unsettled, and a value a newer node wrote reads as Default");
    }

    [Test]
    public void TheColumnWritesNullForDefaultAndMarksAnUnsettledCancelPrevious()
    {
        SimpleTriggerImpl trigger = new();
        OverlapPolicyColumn.ToDbValue(trigger).Should().Be(DBNull.Value, "Default is written as a 4.2 node writes the row");

        trigger.OverlapPolicy = OverlapPolicy.CancelPrevious;
        trigger.OverlapPolicyUnsettled = true;
        OverlapPolicyColumn.ToDbValue(trigger).Should().Be(19);

        trigger.OverlapPolicy = OverlapPolicy.Skip;
        OverlapPolicyColumn.ToDbValue(trigger).Should().Be(1, "the mark belongs to CancelPrevious alone");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The HTTP body that updates a trigger's details
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void AnUpdateCarriesThePolicyOverTheWireByName()
    {
        UpdateTriggerDetailsRequest request = UpdateTriggerDetailsRequest.Create(
            new TriggerDetailsUpdate().WithOverlapPolicy(OverlapPolicy.BufferOne));

        request.HasOverlapPolicy.Should().BeTrue();
        request.OverlapPolicy.Should().Be("BufferOne");

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new UpdateTriggerDetailsRequest.Converter() } };
        string body = JsonSerializer.Serialize(request, options);
        body.Should().Contain("\"overlapPolicy\":\"BufferOne\"");

        UpdateTriggerDetailsRequest read = JsonSerializer.Deserialize<UpdateTriggerDetailsRequest>(body, options)!;
        read.Validate().Should().BeEmpty();

        TriggerDetailsUpdate update = read.AsUpdate();
        update.HasOverlapPolicy.Should().BeTrue();
        update.OverlapPolicy.Should().Be(OverlapPolicy.BufferOne);
    }

    [Test]
    public void AnUpdateThatLeavesThePolicyAloneSaysNothingAboutIt()
    {
        UpdateTriggerDetailsRequest request = UpdateTriggerDetailsRequest.Create(new TriggerDetailsUpdate().WithPriority(3));

        request.HasOverlapPolicy.Should().BeFalse();
        request.AsUpdate().HasOverlapPolicy.Should().BeFalse("an update changes only what it names");
    }

    [Test]
    public void AnUpdateNamingNoPolicyIsRefused()
    {
        UpdateTriggerDetailsRequest request = new() { HasOverlapPolicy = true, OverlapPolicy = "Sometimes" };

        request.Validate().Should().ContainSingle().Which.Should().StartWith("Overlap policy 'Sometimes' is not an overlap policy");
    }

    [Test]
    public void AMisfireHistoryRowCarriesItsReasonOverTheWire()
    {
        MisfireHistoryEntry entry = new("s", "node", "g", "t", null, DateTimeOffset.UnixEpoch, null) { Reason = MisfireReason.Overlap };

        MisfireHistoryEntryDto dto = MisfireHistoryEntryDto.Create(entry);
        dto.Reason.Should().Be(MisfireReason.Overlap);
        dto.AsMisfireHistoryEntry("s").Reason.Should().Be(MisfireReason.Overlap);

        (dto with { Reason = MisfireReason.Missed }).AsMisfireHistoryEntry("s").Reason.Should().Be(MisfireReason.Missed);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The history
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task ASkippedFiringIsRecordedAsAnOverlapBesideTheMisfires()
    {
        DateTimeOffset now = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();

        ServiceCollection services = new();
        services.AddSingleton(store);
        await using ServiceProvider provider = services.BuildServiceProvider();

        ExecutionHistoryPlugin plugin = new(provider, new FakeTimeProvider(now));

        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("s");
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns("node");

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();
        trigger.NextFireTimeUtc = now.AddMinutes(-1);

        await plugin.TriggerSkipped(trigger, scheduler);

        A.CallTo(() => store.AddMisfire(
                A<MisfireHistoryEntry>.That.Matches(entry =>
                    entry.Reason == MisfireReason.Overlap
                    && entry.TriggerName == "t"
                    && entry.ScheduledFireTimeUtc == now.AddMinutes(-1)
                    && entry.MisfiredAtUtc == now),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }
}
