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

using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

/// <summary>
/// One combination of a stored trigger and a misfire instruction given in some family's vocabulary.
/// </summary>
public sealed class MisfireInstructionFamilyCase
{
    internal TriggerFamily Stored { get; init; }
    internal TriggerFamily Requested { get; init; }

    /// <summary>The family name as it must read in the rejection message.</summary>
    public string StoredName { get; init; }

    /// <summary>The family name the update was phrased in, as it must read in the rejection message.</summary>
    public string RequestedName { get; init; }

    /// <summary>Builds the trigger to store, of the <see cref="Stored" /> family.</summary>
    public Func<TriggerKey, JobKey, IOperableTrigger> CreateTrigger { get; init; }

    /// <summary>Builds the update, whose instruction is phrased in the <see cref="Requested" /> family.</summary>
    public Func<TriggerDetailsUpdate> CreateUpdate { get; init; }

    /// <summary>The raw code the instruction carries. Every case uses the same one - see the class remarks.</summary>
    public int InstructionCode { get; init; }

    internal bool FamiliesAgree => Stored == Requested;

    public override string ToString() => $"{RequestedName} instruction on {StoredName} trigger";
}

/// <summary>
/// The (stored trigger family × misfire-instruction family) matrix that every job store must answer
/// the same way: an instruction phrased in the stored trigger's own family is applied, and one
/// phrased in any other family is rejected.
/// </summary>
/// <remarks>
/// Every case carries instruction code 2, which is in range for all five families. That is the whole
/// point: <see cref="Quartz.Impl.Triggers.TriggerBase" />'s own range check cannot tell the twenty
/// wrong combinations from the five right ones, so a store that skips the family check silently
/// stores a policy the caller never asked for.
/// <para>
/// Both <c>RAMJobStore</c> and <c>AdoJobStoreBase</c> run against this list, so the two stores are
/// provably in agreement rather than merely believed to be.
/// </para>
/// </remarks>
public static class MisfireInstructionFamilyCases
{
    private static readonly Func<TriggerKey, JobKey, IOperableTrigger> Simple = (triggerKey, jobKey) =>
        (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

    private static readonly Func<TriggerKey, JobKey, IOperableTrigger> Cron = (triggerKey, jobKey) =>
        (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithCronSchedule("0/30 * * * * ?")
            .Build();

    private static readonly Func<TriggerKey, JobKey, IOperableTrigger> CalendarInterval = (triggerKey, jobKey) =>
        (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithCalendarIntervalSchedule(x => x.WithInterval(1, IntervalUnit.Day))
            .Build();

    private static readonly Func<TriggerKey, JobKey, IOperableTrigger> DailyTimeInterval = (triggerKey, jobKey) =>
        (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithDailyTimeIntervalSchedule(x => x.WithInterval(15, IntervalUnit.Minute))
            .Build();

    private static readonly Func<TriggerKey, JobKey, IOperableTrigger> Recurrence = (triggerKey, jobKey) =>
        (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithRecurrenceSchedule("FREQ=DAILY")
            .Build();

    /// <summary>One family: the trigger that belongs to it, and the instruction that names it.</summary>
    private sealed record Family(
        TriggerFamily Id,
        string Name,
        Func<TriggerKey, JobKey, IOperableTrigger> CreateTrigger,
        Func<TriggerDetailsUpdate> CreateUpdate);

    /// <summary>
    /// The five families. The instructions all carry code 2, so only the family - never the number -
    /// can tell them apart.
    /// </summary>
    private static readonly Family[] Families =
    [
        new Family(TriggerFamily.Simple, "simple", Simple,
            () => new TriggerDetailsUpdate().WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithExistingCount)),
        new Family(TriggerFamily.Cron, "cron", Cron,
            () => new TriggerDetailsUpdate().WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)),
        new Family(TriggerFamily.CalendarInterval, "calendar interval", CalendarInterval,
            () => new TriggerDetailsUpdate().WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.DoNothing)),
        new Family(TriggerFamily.DailyTimeInterval, "daily time interval", DailyTimeInterval,
            () => new TriggerDetailsUpdate().WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.DoNothing)),
        new Family(TriggerFamily.Recurrence, "recurrence", Recurrence,
            () => new TriggerDetailsUpdate().WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.DoNothing)),
    ];

    /// <summary>The full 5 × 5 matrix.</summary>
    public static IEnumerable<MisfireInstructionFamilyCase> All()
    {
        foreach (Family stored in Families)
        {
            foreach (Family requested in Families)
            {
                yield return new MisfireInstructionFamilyCase
                {
                    Stored = stored.Id,
                    Requested = requested.Id,
                    StoredName = stored.Name,
                    RequestedName = requested.Name,
                    CreateTrigger = stored.CreateTrigger,
                    CreateUpdate = requested.CreateUpdate,
                    InstructionCode = 2
                };
            }
        }
    }

    /// <summary>The five diagonal cases, where the update names the stored trigger's own family.</summary>
    public static IEnumerable<MisfireInstructionFamilyCase> Matching() => All().Where(x => x.FamiliesAgree);

    /// <summary>The twenty off-diagonal cases, which every store must reject.</summary>
    public static IEnumerable<MisfireInstructionFamilyCase> Mismatched() => All().Where(x => !x.FamiliesAgree);
}
