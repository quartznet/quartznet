using FakeItEasy;

using Quartz.Dashboard.Services;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The editor's one translation: fields as text into a <see cref="TriggerDetailsUpdate" /> that names what
/// changed, in the family the trigger belongs to.
/// </summary>
public sealed class TriggerDetailsFormTest
{
    [Test]
    public void AFormLeftAloneAsksForNothing()
    {
        TriggerDetailsForm form = TriggerDetailsForm.From(Cron());

        TriggerDetailsEdit edit = form.Build();

        edit.Update.Should().BeNull("nothing changed, and an empty patch is not worth a round trip");
        edit.Changes.Should().BeEmpty();
        edit.Errors.Should().BeEmpty();
    }

    [TestCase("simple", typeof(SimpleTriggerMisfireInstruction), "FireNow")]
    [TestCase("calendar-interval", typeof(CalendarIntervalTriggerMisfireInstruction), "DoNothing")]
    [TestCase("daily", typeof(DailyTimeIntervalTriggerMisfireInstruction), "DoNothing")]
    [TestCase("recurrence", typeof(RecurrenceTriggerMisfireInstruction), "DoNothing")]
    [TestCase("cron", typeof(CronTriggerMisfireInstruction), "DoNothing")]
    public void AMisfireInstructionIsSentInTheTriggersOwnFamily(string kind, Type instructions, string name)
    {
        ITrigger trigger = kind switch
        {
            "simple" => TriggerBuilder.Create().WithIdentity("t").ForJob("j").WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever()).Build(),
            "calendar-interval" => TriggerBuilder.Create().WithIdentity("t").ForJob("j").WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Day)).Build(),
            "daily" => TriggerBuilder.Create().WithIdentity("t").ForJob("j").WithDailyTimeIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Hour)).Build(),
            "recurrence" => TriggerBuilder.Create().WithIdentity("t").ForJob("j").WithRecurrenceSchedule("FREQ=DAILY").Build(),
            _ => Cron()
        };
        TriggerDetailsForm form = TriggerDetailsForm.From(trigger);
        int code = Convert.ToInt32(Enum.Parse(instructions, name), System.Globalization.CultureInfo.InvariantCulture);

        form.MisfireOptions.Select(option => option.Name).Should().BeEquivalentTo(Enum.GetNames(instructions));
        form.MisfireInstruction = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
        TriggerDetailsEdit edit = form.Build();

        edit.Update.Should().NotBeNull();
        edit.Update!.MisfireInstructionCode.Should().Be(code);
        edit.Update.MisfireInstructionFamily.Should().NotBeNull(
            "the typed overload names the family, which is what lets the store refuse a code meant for another one");
        edit.Changes.Should().ContainSingle().Which.Should().EndWith("→ " + name);
        TriggerDetailsForm.MisfireNameOf(trigger).Should().Be(form.MisfireName(trigger.MisfireInstructionCode));
    }

    [Test]
    public void EachFieldThatChangedIsNamedInTheLog()
    {
        TriggerDetailsForm form = TriggerDetailsForm.From(Cron());

        form.ExecutionGroup = "reports";
        form.RetryPolicy = "exp;5;00:00:10;2";
        form.PreferredNodeMode = TriggerDetailsForm.NodeAuto;
        TriggerDetailsEdit edit = form.Build();

        edit.Errors.Should().BeEmpty();
        edit.Update!.ExecutionGroup.Should().Be("reports");
        edit.Update.RetryPolicy.Should().Be(RetryPolicy.Exponential(5, TimeSpan.FromSeconds(10)));
        edit.Update.PreferredNode.Should().Be(PreferredNode.Auto);
        edit.Changes.Should().Equal(
            "execution group (none) → reports",
            "retry policy (none) → exp;5;00:00:10;2",
            "preferred node (none) → auto");
    }

    [Test]
    public void ClearingAPinAndARetryPolicyIsAChangeToNothing()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("t")
            .ForJob("j")
            .WithPreferredNode(PreferredNode.For("node-a"))
            .WithRetryPolicy(RetryPolicy.Fixed(2, TimeSpan.FromMinutes(1)))
            .WithCronSchedule("0 0 1 * * ?")
            .Build();
        TriggerDetailsForm form = TriggerDetailsForm.From(trigger);
        form.PreferredNodeMode.Should().Be(TriggerDetailsForm.NodeNamed);
        form.PreferredNodeName.Should().Be("node-a");

        form.PreferredNodeMode = TriggerDetailsForm.NodeNone;
        form.RetryPolicy = " ";
        TriggerDetailsEdit edit = form.Build();

        edit.Update!.HasPreferredNode.Should().BeTrue();
        edit.Update.PreferredNode.IsNone.Should().BeTrue();
        edit.Update.HasRetryPolicy.Should().BeTrue();
        edit.Update.RetryPolicy.Should().BeNull("a blank policy is no retries");
        edit.Changes.Should().Contain("preferred node node-a → (none)");
    }

    [Test]
    public void AnAutomaticPinSomeNodeClaimedIsLeftAloneWhileTheChoiceStaysOnAuto()
    {
        ITrigger trigger = A.Fake<ITrigger>();
        A.CallTo(() => trigger.PreferredNode).Returns(PreferredNode.ClaimedBy("node-b"));
        A.CallTo(() => trigger.JobDataMap).Returns(new JobDataMap());
        TriggerDetailsForm form = TriggerDetailsForm.From(trigger);

        form.PreferredNodeMode.Should().Be(TriggerDetailsForm.NodeAuto);
        form.Build().Update.Should().BeNull("auto is still auto, and re-sending it would ask for nothing");
    }

    [Test]
    public void WhatTheFieldsGetWrongIsSaidPerField()
    {
        TriggerDetailsForm form = TriggerDetailsForm.From(Cron());

        form.MisfireInstruction = "soon";
        form.PreferredNodeMode = TriggerDetailsForm.NodeNamed;
        form.PreferredNodeName = "*";
        form.OverlapPolicy = "Sometimes";
        TriggerDetailsEdit edit = form.Build();

        edit.Update.Should().BeNull("an update with any field wrong is not sent");
        edit.Errors.Should().HaveCount(3);
        edit.Errors[0].Should().Be("The misfire instruction must be a whole number.");
        edit.Errors[1].Should().StartWith("Preferred node: ").And.Contain("reserved");
        edit.Errors[2].Should().Be("Overlap policy: 'Sometimes' is not an overlap policy.");

        form.OverlapPolicy = OverlapPolicy.Default.ToString();
        form.PreferredNodeMode = "somewhere";
        form.Build().Errors.Should().Contain("Preferred node: 'somewhere' is not a choice.");
    }

    [Test]
    public void TheMisfireThresholdIsEditedAsATimeSpanAndClearedWithBlank()
    {
        TriggerDetailsForm form = TriggerDetailsForm.From(Cron());
        form.MisfireThreshold.Should().BeEmpty("a trigger on the store's threshold shows nothing to edit");
        form.MisfireThresholdUnavailable.Should().BeNull();

        form.MisfireThreshold = "00:05:00";
        TriggerDetailsEdit set = form.Build();

        set.Errors.Should().BeEmpty();
        set.Update!.HasMisfireThreshold.Should().BeTrue();
        set.Update.MisfireThreshold.Should().Be(TimeSpan.FromMinutes(5));
        set.Changes.Should().Equal("misfire threshold store's → 00:05:00");

        ITrigger tolerant = TriggerBuilder.Create().WithIdentity("t").ForJob("j").WithCronSchedule("0 0 1 * * ?")
            .WithMisfireThreshold(TimeSpan.FromMinutes(5)).Build();
        TriggerDetailsForm clearing = TriggerDetailsForm.From(tolerant);
        clearing.MisfireThreshold.Should().Be("00:05:00");
        clearing.Build().Update.Should().BeNull("an unchanged threshold is not an edit");

        clearing.MisfireThreshold = " ";
        TriggerDetailsEdit cleared = clearing.Build();
        cleared.Update!.HasMisfireThreshold.Should().BeTrue();
        cleared.Update.MisfireThreshold.Should().BeNull("blank puts the trigger back on the store's threshold");
        cleared.Changes.Should().Equal("misfire threshold 00:05:00 → store's");
    }

    [Test]
    public void AThresholdThatIsNotOneIsSaidAndNotSent()
    {
        TriggerDetailsForm form = TriggerDetailsForm.From(Cron());

        form.MisfireThreshold = "soon";
        TriggerDetailsEdit edit = form.Build();

        edit.Update.Should().BeNull();
        edit.Errors.Should().ContainSingle().Which.Should().StartWith("Misfire threshold: 'soon' is not a misfire threshold.");
    }

    [Test]
    public void ATriggerOutsideTriggerBaseHasItsThresholdDisabledWithTheReason()
    {
        ITrigger trigger = A.Fake<ITrigger>();
        A.CallTo(() => trigger.JobDataMap).Returns(new JobDataMap());
        TriggerDetailsForm form = TriggerDetailsForm.From(trigger);

        form.MisfireThresholdUnavailable.Should().Contain("cannot carry a misfire threshold");
        form.MisfireThreshold = "00:05:00";
        form.Build().Update.Should().BeNull("a threshold such a trigger cannot carry is not offered, so it is not sent");
        TriggerDetailsForm.MisfireThresholdOf(trigger).Should().Be("Store's");
    }

    [Test]
    public void ASimpleTriggerIsOfferedMeasuringFromCompletionAndACronTriggerIsNot()
    {
        TriggerDetailsForm form = TriggerDetailsForm.From(TriggerBuilder.Create().WithIdentity("t").ForJob("j")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())
            .Build());
        form.OffersMeasureIntervalFromCompletion.Should().BeTrue();
        form.MeasureIntervalFromCompletion.Should().BeFalse();
        form.Build().Update.Should().BeNull("an unchanged setting is not an edit");

        form.MeasureIntervalFromCompletion = true;
        TriggerDetailsEdit edit = form.Build();

        edit.Errors.Should().BeEmpty();
        edit.Update!.HasMeasureIntervalFromCompletion.Should().BeTrue();
        edit.Update.MeasureIntervalFromCompletion.Should().BeTrue();
        edit.Changes.Should().Equal("interval measured from scheduled time → completion");

        TriggerDetailsForm cron = TriggerDetailsForm.From(Cron());
        cron.OffersMeasureIntervalFromCompletion.Should().BeFalse("only a simple trigger has an interval to measure");
        cron.MeasureIntervalFromCompletion = true;
        cron.Build().Update.Should().BeNull("a setting the trigger cannot carry is not offered, so it is not sent");
    }

    private static ITrigger Cron()
    {
        return TriggerBuilder.Create().WithIdentity("t").ForJob("j").WithCronSchedule("0 0 1 * * ?").Build();
    }
}
