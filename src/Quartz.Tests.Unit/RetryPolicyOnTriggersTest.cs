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

using System.Reflection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// How a retry policy sits on a trigger: what the builders carry, what a rebuild keeps, and what the
/// blob shape has to hold for a trigger to come back with its policy.
/// </summary>
[TestFixture]
public class RetryPolicyOnTriggersTest
{
    private static readonly RetryPolicy policy = RetryPolicy.Exponential(4, TimeSpan.FromSeconds(10), 2, TimeSpan.FromMinutes(5));

    [Test]
    public void ATriggerHasNoRetryPolicyUnlessItIsGivenOne()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .Build();

        trigger.RetryPolicy.Should().BeNull("retrying is opt-in; a trigger that says nothing does what it always did");
        trigger.RetryAttempt.Should().Be(0);
    }

    [Test]
    public void TheBuilderPutsThePolicyOnTheTrigger()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .WithRetryPolicy(policy)
            .Build();

        trigger.RetryPolicy.Should().Be(policy);
        trigger.RetryAttempt.Should().Be(0, "a trigger that has never fired has retried nothing");
    }

    [Test]
    public void RebuildingATriggerKeepsItsPolicyAndDropsTheAttempt()
    {
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .WithRetryPolicy(policy)
            .Build();
        trigger.RetryAttempt = 3;

        ITrigger rebuilt = trigger.GetTriggerBuilder().Build();

        rebuilt.RetryPolicy.Should().Be(policy, "the policy is part of the definition a builder round-trips");
        rebuilt.RetryAttempt.Should().Be(0,
            "the attempt counts retries of the occurrence being executed, and a rebuilt trigger has no occurrence "
            + "in flight — exactly as it has no NextFireTimeUtc");
    }

    [Test]
    public void ABuilderWithNoPolicyClearsOneTheTriggerHad()
    {
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .WithRetryPolicy(policy)
            .WithRetryPolicy(null)
            .Build();

        trigger.RetryPolicy.Should().BeNull(
            "a builder-built trigger fully defines its retry policy, so a definition without one clears a stored "
            + "policy when it replaces an existing trigger — the same rule the execution group and the pin follow");
    }

    [Test]
    public void TheConfiguratorCarriesThePolicyToo()
    {
        ITriggerConfigurator<IJob> configurator = TriggerBuilder.Create();

        configurator
            .WithIdentity("t", "g")
            .WithRetryPolicy(policy)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)));

        ((TriggerBuilder<IJob>) configurator).Build().RetryPolicy.Should().Be(policy);
    }

    [Test]
    public void ANegativeAttemptIsRefused()
    {
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .Build();

        Action act = () => trigger.RetryAttempt = -1;

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void CloningATriggerCarriesBothHalvesOfTheRetryState()
    {
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .WithRetryPolicy(policy)
            .Build();
        trigger.RetryAttempt = 2;

        ITrigger clone = trigger.Clone();

        clone.RetryPolicy.Should().Be(policy);
        clone.RetryAttempt.Should().Be(2,
            "RAMJobStore hands out clones, and a clone that forgot where the occurrence was would restart its retries");
    }

    /// <summary>
    /// The <c>[Serializable]</c> shape, which is what a 3.x <c>BLOB_TRIGGERS</c> blob is read through.
    /// Serialization there is field-based and runs no constructor, so the field names are the contract
    /// and a field absent from an old blob simply stays at its default.
    /// </summary>
    [Test]
    public void TheBlobShapeCarriesThePolicyAsAStringAndTheAttemptAsANumber()
    {
        FieldInfo storedPolicy = typeof(TriggerBase).GetField("retryPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo storedAttempt = typeof(TriggerBase).GetField("retryAttempt", BindingFlags.Instance | BindingFlags.NonPublic);

        storedPolicy.Should().NotBeNull("the serialized field is what a blob carries");
        storedPolicy!.FieldType.Should().Be<string>(
            "the policy is held as its stored string so the blob holds a primitive: a RetryPolicy has no public "
            + "constructor, and field-based deserialization would have nothing to rebuild one with");
        storedPolicy.GetCustomAttribute<NonSerializedAttribute>().Should().BeNull("the policy has to travel in the blob");

        storedAttempt.Should().NotBeNull();
        storedAttempt!.FieldType.Should().Be<int>();
        storedAttempt.GetCustomAttribute<NonSerializedAttribute>().Should().BeNull();

        FieldInfo cached = typeof(TriggerBase).GetField("retryPolicyValue", BindingFlags.Instance | BindingFlags.NonPublic);
        cached.Should().NotBeNull();
        cached!.GetCustomAttribute<NonSerializedAttribute>().Should().NotBeNull(
            "the parsed value is derived from the field that is serialized, and writing both would put the same "
            + "state in a blob twice");
    }

    /// <summary>
    /// The System.Text.Json path, which is what a 4.x <c>BLOB_TRIGGERS</c> blob and the HTTP wire both
    /// go through.
    /// </summary>
    [Test]
    public void ThePolicyAndTheAttemptSurviveASystemTextJsonRoundTrip()
    {
        IMutableTrigger trigger = (IMutableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .WithRetryPolicy(policy)
            .Build();
        trigger.RetryAttempt = 2;

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();

        byte[] bytes = serializer.Serialize(trigger);
        IOperableTrigger restored = serializer.Deserialize<IOperableTrigger>(bytes)!;

        restored.RetryPolicy.Should().Be(policy);
        restored.RetryAttempt.Should().Be(2);
    }

    [Test]
    public void ATriggerWithNoPolicySurvivesASystemTextJsonRoundTrip()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .Build();

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();

        IOperableTrigger restored = serializer.Deserialize<IOperableTrigger>(serializer.Serialize(trigger))!;

        restored.RetryPolicy.Should().BeNull();
        restored.RetryAttempt.Should().Be(0);
    }

    [Test]
    public void ATriggerFromAnOlderBlobReadsAsHavingNoPolicy()
    {
        // What field-based deserialization of a blob written before the fields existed leaves behind:
        // both at their defaults, on an instance no constructor ran for.
        TriggerBase trigger = (TriggerBase) System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SimpleTriggerImpl));

        trigger.RetryPolicy.Should().BeNull();
        trigger.RetryAttempt.Should().Be(0);
    }
}
