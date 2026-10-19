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

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Util;

namespace Quartz.Tests.Unit;

/// <summary>
/// How a misfire threshold sits on a trigger: what the builders take and refuse, what a rebuild keeps,
/// what every shape a trigger travels in writes, and what the default interface members answer (#4070).
/// </summary>
[TestFixture]
public class MisfireThresholdOnTriggersTest
{
    [Test]
    public void ATriggerIsOnTheStoresThresholdUnlessItIsGivenOne()
    {
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();

        trigger.MisfireThreshold.Should().BeNull("a trigger nobody gave a threshold misfires by the store's, as every trigger did before");
    }

    [Test]
    public void TheThresholdIsCarriedByTheBuilderAndKeptByARebuildAndAClone()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t", "g")
            .ForJob("j", "jg")
            .WithCronSchedule("0 0 3 * * ?")
            .WithMisfireThreshold(TimeSpan.FromMinutes(5))
            .Build();

        trigger.MisfireThreshold.Should().Be(TimeSpan.FromMinutes(5));
        trigger.GetTriggerBuilder().Build().MisfireThreshold.Should().Be(TimeSpan.FromMinutes(5),
            "rebuilding a trigger is how a reschedule keeps what the caller did not change");
        trigger.Clone().MisfireThreshold.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void ARebuiltDefinitionWithoutAThresholdPutsTheTriggerBackOnTheStores()
    {
        TriggerBuilder<IJob> builder = TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg");

        builder.Build().MisfireThreshold.Should().BeNull(
            "a builder-built trigger fully defines its threshold, so one that replaces a stored trigger clears a threshold it does not name");
    }

    [TestCase(0L)]
    [TestCase(-10_000_000L)]
    [TestCase(10_000_001L)]
    [TestCase(5L)]
    public void AThresholdThatIsNotPositiveWholeMillisecondsIsRefusedWhereverItIsGiven(long ticks)
    {
        TimeSpan threshold = TimeSpan.FromTicks(ticks);

        Action builder = () => TriggerBuilder.Create().WithMisfireThreshold(threshold);
        Action property = () => new SimpleTriggerImpl().MisfireThreshold = threshold;
        Action update = () => new TriggerDetailsUpdate().WithMisfireThreshold(threshold);

        builder.Should().Throw<ArgumentOutOfRangeException>("a persistent store keeps whole milliseconds and a threshold of zero misfires everything");
        property.Should().Throw<ArgumentOutOfRangeException>();
        update.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ATriggerOutsideTheBuiltInHierarchyCannotBeGivenAThresholdByTheBuilder()
    {
        IMutableTrigger custom = A.Fake<IMutableTrigger>();
        IScheduleBuilder schedule = A.Fake<IScheduleBuilder>();
        A.CallTo(() => schedule.Build()).Returns(custom);

        Action withThreshold = () => TriggerBuilder.Create().WithSchedule(schedule).WithMisfireThreshold(TimeSpan.FromSeconds(5)).Build();

        withThreshold.Should().Throw<NotSupportedException>(
                "a threshold the trigger cannot carry would be dropped in silence")
            .WithMessage("*TriggerBase*misfire threshold*");
    }

    [Test]
    public void TheInterfaceDefaultsAnswerWithoutAnImplementation()
    {
        ITrigger trigger = A.Fake<ITrigger>();
        A.CallTo(() => trigger.MisfireThreshold).CallsBaseMethod();
        trigger.MisfireThreshold.Should().BeNull("a trigger written against 4.5 has no threshold of its own, which is the store's");

        ITriggerConfigurator<IJob> configurator = A.Fake<ITriggerConfigurator<IJob>>();
        A.CallTo(() => configurator.WithMisfireThreshold(A<TimeSpan>._)).CallsBaseMethod();
        Action configure = () => configurator.WithMisfireThreshold(TimeSpan.FromSeconds(5));
        configure.Should().Throw<NotSupportedException>(
            "a configurator that predates the member says so rather than building a trigger on the wrong threshold");
    }

    [Test]
    public void TheConfiguratorTheRegistrationsUseCarriesIt()
    {
        ITriggerConfigurator<IJob> configurator = TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg");

        configurator.WithMisfireThreshold(TimeSpan.FromSeconds(90));

        ((TriggerBuilder<IJob>) configurator).Build().MisfireThreshold.Should().Be(TimeSpan.FromSeconds(90));
    }

    [Test]
    public void AnUpdateDetectsThatItChangesNothing()
    {
        new TriggerDetailsUpdate().ChangesNothing.Should().BeTrue();
        new TriggerDetailsUpdate().WithMisfireThreshold(null).ChangesNothing.Should().BeFalse(
            "clearing a threshold is a change, which the stores must not answer as done without making it");
    }

    [Test]
    public void ATriggerOutsideTheHierarchyRefusesAnUpdatedThreshold()
    {
        TriggerDetailsUpdate update = new TriggerDetailsUpdate().WithMisfireThreshold(TimeSpan.FromSeconds(5));
        Action ensure = () => update.EnsureMisfireThresholdCanBeCarried(A.Fake<ITrigger>(), new TriggerKey("t", "g"));

        ensure.Should().Throw<JobPersistenceException>().WithMessage("*cannot carry a misfire threshold*");
        new TriggerDetailsUpdate().EnsureMisfireThresholdCanBeCarried(A.Fake<ITrigger>(), new TriggerKey("t", "g"));
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The rule both stores decide by
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void TheMisfireTimeIsNowLessTheTriggersOwnThresholdOrTheStores()
    {
        DateTimeOffset now = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);
        SimpleTriggerImpl own = new() { MisfireThreshold = TimeSpan.FromSeconds(5) };
        SimpleTriggerImpl none = new();

        MisfireThresholds.MisfireTime(now, own, TimeSpan.FromMinutes(1)).Should().Be(now.AddSeconds(-5));
        MisfireThresholds.MisfireTime(now, none, TimeSpan.FromMinutes(1)).Should().Be(now.AddMinutes(-1));
        MisfireThresholds.MisfireTime(now, TimeSpan.Zero).Should().Be(now, "a threshold that is not positive counts from now, as both stores always have");
        MisfireThresholds.MisfireTime(now, TimeSpan.MaxValue).Should().Be(DateTimeOffset.MinValue,
            "a threshold longer than the calendar misfires nothing rather than throwing");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The shapes a trigger travels in
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void TheThresholdSurvivesTheSystemTextJsonRoundTrip()
    {
        SystemTextJsonObjectSerializer serializer = new();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("t", "g").ForJob("j", "jg").WithMisfireThreshold(TimeSpan.FromMinutes(5)).Build();

        byte[] payload = serializer.Serialize(trigger);

        Encoding.UTF8.GetString(payload).Should().Contain("\"MisfireThreshold\":\"00:05:00\"",
            "the threshold is written as the TimeSpan's invariant form, which a reader of the payload can read");
        serializer.Deserialize<IOperableTrigger>(payload)!.MisfireThreshold.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void ATriggerOnTheStoresThresholdWritesTheSystemTextJsonPayloadItAlwaysDid()
    {
        SystemTextJsonObjectSerializer serializer = new();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();

        Encoding.UTF8.GetString(serializer.Serialize(trigger)).Should().NotContain("MisfireThreshold",
            "a trigger with no threshold of its own is written exactly as a 4.5 node wrote it");
    }

    [TestCase("\"00:00:30\"", 30_000L)]
    [TestCase("\"nonsense\"", null)]
    [TestCase("\"-00:00:30\"", null)]
    [TestCase("30", null)]
    [TestCase("null", null)]
    public void TheSystemTextJsonReaderIsForgiving(string written, long? expectedMilliseconds)
    {
        SystemTextJsonObjectSerializer serializer = new();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("t", "g").ForJob("j", "jg").Build();

        string payload = Encoding.UTF8.GetString(serializer.Serialize(trigger));
        payload = payload[..^1] + $",\"MisfireThreshold\":{written}}}";

        serializer.Deserialize<IOperableTrigger>(Encoding.UTF8.GetBytes(payload))!.MisfireThreshold.Should().Be(
            expectedMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
            "a value this version cannot take reads as the store's threshold rather than failing the trigger");
    }

    [TestCase(null, null)]
    [TestCase("00:05:00", 300_000L)]
    [TestCase(" 00:00:01.250 ", 1_250L)]
    public void AFileStatesAThresholdAsATimeSpan(string? written, long? expectedMilliseconds)
    {
        SchedulingFileValues.ReadMisfireThreshold(written, "Trigger 't'").Should().Be(
            expectedMilliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null);
    }

    [TestCase("5 minutes")]
    [TestCase("00:00:00")]
    [TestCase("-00:01:00")]
    [TestCase("00:00:00.0001")]
    public void AFileStatingNoThresholdIsRefused(string written)
    {
        Action read = () => SchedulingFileValues.ReadMisfireThreshold(written, "Trigger 't'");

        read.Should().Throw<SchedulerConfigException>("a threshold that is not one would put the trigger on another than it asked for")
            .WithMessage($"Trigger 't': '{written}' is not a misfire threshold. State a TimeSpan*");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The attributes
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void TheAttributesTakeAThresholdAsATimeSpanString()
    {
        new CronTriggerAttribute("0 0 * * * ?") { MisfireThreshold = "00:05:00" }.MisfireThreshold.Should().Be("00:05:00");
        new SimpleTriggerAttribute("00:01:00") { MisfireThreshold = "00:00:30" }.MisfireThreshold.Should().Be("00:00:30");
        new SimpleTriggerAttribute("00:01:00").MisfireThreshold.Should().BeNull("an attribute that names none leaves the trigger on the store's");
    }

    [TestCase("soon")]
    [TestCase("00:00:00")]
    [TestCase("00:00:00.0001")]
    public void TheAttributesRefuseWhatTheGeneratorRefuses(string written)
    {
        Action cron = () => _ = new CronTriggerAttribute("0 0 * * * ?") { MisfireThreshold = written };
        Action simple = () => _ = new SimpleTriggerAttribute("00:01:00") { MisfireThreshold = written };

        cron.Should().Throw<ArgumentException>().WithMessage("*misfire threshold*");
        simple.Should().Throw<ArgumentException>().WithMessage("*misfire threshold*");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // The HTTP body that updates a trigger's details
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public void AnUpdateCarriesTheThresholdOverTheWire()
    {
        UpdateTriggerDetailsRequest request = UpdateTriggerDetailsRequest.Create(
            new TriggerDetailsUpdate().WithMisfireThreshold(TimeSpan.FromMinutes(5)));

        request.HasMisfireThreshold.Should().BeTrue();
        request.MisfireThreshold.Should().Be("00:05:00");

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new UpdateTriggerDetailsRequest.Converter() } };
        string body = JsonSerializer.Serialize(request, options);
        body.Should().Contain("\"misfireThreshold\":\"00:05:00\"");

        UpdateTriggerDetailsRequest read = JsonSerializer.Deserialize<UpdateTriggerDetailsRequest>(body, options)!;
        read.Validate().Should().BeEmpty();

        TriggerDetailsUpdate update = read.AsUpdate();
        update.HasMisfireThreshold.Should().BeTrue();
        update.MisfireThreshold.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void AnUpdateClearsTheThresholdWithANullMember()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new UpdateTriggerDetailsRequest.Converter() } };
        UpdateTriggerDetailsRequest request = UpdateTriggerDetailsRequest.Create(new TriggerDetailsUpdate().WithMisfireThreshold(null));

        string body = JsonSerializer.Serialize(request, options);
        body.Should().Contain("\"misfireThreshold\":null", "a present null clears, where an absent member leaves the value alone");

        TriggerDetailsUpdate update = JsonSerializer.Deserialize<UpdateTriggerDetailsRequest>(body, options)!.AsUpdate();
        update.HasMisfireThreshold.Should().BeTrue();
        update.MisfireThreshold.Should().BeNull();

        UpdateTriggerDetailsRequest.Create(new TriggerDetailsUpdate().WithPriority(3)).AsUpdate().HasMisfireThreshold.Should().BeFalse(
            "an update changes only what it names");
    }

    [Test]
    public void AnUpdateNamingNoThresholdIsRefused()
    {
        UpdateTriggerDetailsRequest request = new() { HasMisfireThreshold = true, MisfireThreshold = "soon" };

        request.Validate().Should().ContainSingle().Which.Should().StartWith("Misfire threshold 'soon' is not a misfire threshold");
    }
}
