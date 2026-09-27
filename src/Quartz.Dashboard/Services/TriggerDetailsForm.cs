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

using Quartz.Impl.Triggers;

namespace Quartz.Dashboard.Services;

/// <summary>
/// The Trigger Detail editor's fields, as the text the inputs hold, and the one place they become a
/// <see cref="TriggerDetailsUpdate" />.
/// </summary>
/// <remarks>
/// <para>
/// The update is a patch: a field goes on it only when it differs from the trigger the page loaded. A
/// field left alone is not written back, so a value somebody changed after the page was read survives a
/// save of something else — and the store's misfire family check is asked only of an instruction that
/// was changed.
/// </para>
/// <para>
/// Every field is text because an input is: a priority that is not a number is a message beside the
/// field, not a binding that silently keeps the old value.
/// </para>
/// </remarks>
internal sealed class TriggerDetailsForm
{
    /// <summary>The preferred-node choice that clears the pin.</summary>
    public const string NodeNone = "none";

    /// <summary>The preferred-node choice that pins to whichever node fires first.</summary>
    public const string NodeAuto = "auto";

    /// <summary>The preferred-node choice that names the node.</summary>
    public const string NodeNamed = "node";

    private const string Nothing = "(none)";

    private readonly ITrigger original;

    private TriggerDetailsForm(ITrigger original)
    {
        this.original = original;

        Description = original.Description ?? string.Empty;
        Priority = original.Priority.ToString(CultureInfo.InvariantCulture);
        CalendarName = original.CalendarName ?? string.Empty;
        MisfireInstruction = original.MisfireInstructionCode.ToString(CultureInfo.InvariantCulture);
        ExecutionGroup = original.ExecutionGroup ?? string.Empty;
        RetryPolicy = original.RetryPolicy?.ToStoredString() ?? string.Empty;
        PreferredNodeMode = ModeOf(original.PreferredNode);
        PreferredNodeName = original.PreferredNode is { IsAutomatic: false, Node: { } node } ? node : string.Empty;
        OverlapPolicy = original.OverlapPolicy.ToString();
        JobDataMap = Copy(original.JobDataMap);
        MisfireOptions = OptionsFor(original);

        // TriggerDetailsUpdate refuses a policy for a trigger outside TriggerBase before it changes anything,
        // so the field is disabled with that reason rather than offered and refused.
        OverlapPolicyUnavailable = original is TriggerBase
            ? null
            : "A " + original.GetType().Name + " does not derive from TriggerBase, so it cannot carry an overlap policy.";
    }

    /// <summary>
    /// A form holding what <paramref name="trigger" /> holds now.
    /// </summary>
    public static TriggerDetailsForm From(ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return new TriggerDetailsForm(trigger);
    }

    public string Description { get; set; }

    public string Priority { get; set; }

    public string CalendarName { get; set; }

    /// <summary>
    /// The misfire instruction as its code, which is what the family's select and the free number field
    /// both hold.
    /// </summary>
    public string MisfireInstruction { get; set; }

    public string ExecutionGroup { get; set; }

    /// <summary>
    /// The retry policy in its stored form, <c>fixed;3;00:05:00</c> and the like, or blank for none.
    /// </summary>
    public string RetryPolicy { get; set; }

    /// <summary>
    /// <see cref="NodeNone" />, <see cref="NodeAuto" /> or <see cref="NodeNamed" />.
    /// </summary>
    public string PreferredNodeMode { get; set; }

    public string PreferredNodeName { get; set; }

    public string OverlapPolicy { get; set; }

    /// <summary>
    /// A copy of the trigger's map, which the map editor writes into. The trigger's own is never touched.
    /// </summary>
    public JobDataMap JobDataMap { get; }

    /// <summary>
    /// The instructions of the trigger's schedule family, or empty for a trigger in none of the five, whose
    /// instruction is then edited as a bare code.
    /// </summary>
    public IReadOnlyList<MisfireOption> MisfireOptions { get; }

    /// <summary>
    /// Why the overlap policy cannot be edited for this trigger, or <see langword="null" /> when it can.
    /// </summary>
    public string? OverlapPolicyUnavailable { get; }

    /// <summary>
    /// The update the fields ask for, the changes it makes in words, and whatever the fields got wrong.
    /// </summary>
    /// <remarks>
    /// <see cref="TriggerDetailsEdit.Update" /> is <see langword="null" /> when anything is wrong or when
    /// nothing was changed; the page posts nothing then.
    /// </remarks>
    public TriggerDetailsEdit Build()
    {
        List<string> errors = [];
        List<string> changes = [];
        TriggerDetailsUpdate update = new();

        string? description = Blank(Description);
        if (!string.Equals(description, Blank(original.Description), StringComparison.Ordinal))
        {
            update.WithDescription(description);
            changes.Add("description");
        }

        if (!int.TryParse(Priority.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority))
        {
            errors.Add("Priority must be a whole number.");
        }
        else if (priority != original.Priority)
        {
            update.WithPriority(priority);
            changes.Add(Changed("priority", Number(original.Priority), Number(priority)));
        }

        string? calendarName = Blank(CalendarName);
        if (!string.Equals(calendarName, Blank(original.CalendarName), StringComparison.Ordinal))
        {
            update.WithCalendarName(calendarName);
            changes.Add(Changed("calendar", original.CalendarName, calendarName));
        }

        BuildMisfireInstruction(update, changes, errors);

        string? executionGroup = Blank(ExecutionGroup);
        if (!string.Equals(executionGroup, Blank(original.ExecutionGroup), StringComparison.Ordinal))
        {
            update.WithExecutionGroup(executionGroup);
            changes.Add(Changed("execution group", original.ExecutionGroup, executionGroup));
        }

        BuildRetryPolicy(update, changes, errors);
        BuildPreferredNode(update, changes, errors);
        BuildOverlapPolicy(update, changes, errors);

        if (!JobDataMap.Equals(Copy(original.JobDataMap)))
        {
            update.WithJobDataMap(Copy(JobDataMap));
            changes.Add("job data map (" + Number(JobDataMap.Count) + " entries)");
        }

        return new TriggerDetailsEdit(errors.Count == 0 && changes.Count > 0 ? update : null, changes, errors);
    }

    /// <summary>
    /// The name an instruction code goes by in the trigger's family, or the bare number outside one.
    /// </summary>
    public string MisfireName(int code) => NameOf(MisfireOptions, code);

    /// <summary>
    /// The name <paramref name="trigger" />'s own misfire instruction goes by, which is what the detail
    /// page shows beside the others.
    /// </summary>
    public static string MisfireNameOf(ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        return NameOf(OptionsFor(trigger), trigger.MisfireInstructionCode);
    }

    private static string NameOf(IReadOnlyList<MisfireOption> options, int code)
    {
        foreach (MisfireOption option in options)
        {
            if (option.Code == code)
            {
                return option.Name;
            }
        }

        return Number(code);
    }

    private void BuildMisfireInstruction(TriggerDetailsUpdate update, List<string> changes, List<string> errors)
    {
        if (!int.TryParse(MisfireInstruction.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
        {
            errors.Add("The misfire instruction must be a whole number.");
            return;
        }

        if (code == original.MisfireInstructionCode)
        {
            return;
        }

        // The family-typed overload wherever there is a family, so that the store can refuse a code meant
        // for another one; the bare code only for a trigger in none of them, which is what it exists for.
        switch (original)
        {
            case ISimpleTrigger:
                update.WithMisfireInstruction((SimpleTriggerMisfireInstruction) code);
                break;
            case ICronTrigger:
                update.WithMisfireInstruction((CronTriggerMisfireInstruction) code);
                break;
            case ICalendarIntervalTrigger:
                update.WithMisfireInstruction((CalendarIntervalTriggerMisfireInstruction) code);
                break;
            case IDailyTimeIntervalTrigger:
                update.WithMisfireInstruction((DailyTimeIntervalTriggerMisfireInstruction) code);
                break;
            case IRecurrenceTrigger:
                update.WithMisfireInstruction((RecurrenceTriggerMisfireInstruction) code);
                break;
            default:
                update.WithMisfireInstructionCode(code);
                break;
        }

        changes.Add(Changed("misfire instruction", MisfireName(original.MisfireInstructionCode), MisfireName(code)));
    }

    private void BuildRetryPolicy(TriggerDetailsUpdate update, List<string> changes, List<string> errors)
    {
        string? text = Blank(RetryPolicy);
        RetryPolicy? policy = null;
        if (text is not null)
        {
            try
            {
                policy = Quartz.RetryPolicy.Parse(text);
            }
            catch (FormatException ex)
            {
                errors.Add("Retry policy: " + ex.Message);
                return;
            }
        }

        if (policy == original.RetryPolicy)
        {
            return;
        }

        update.WithRetryPolicy(policy);
        changes.Add(Changed("retry policy", original.RetryPolicy?.ToStoredString(), policy?.ToStoredString()));
    }

    private void BuildPreferredNode(TriggerDetailsUpdate update, List<string> changes, List<string> errors)
    {
        PreferredNode current = original.PreferredNode;
        string currentMode = ModeOf(current);
        string mode = PreferredNodeMode;

        PreferredNode requested;
        switch (mode)
        {
            case NodeNone:
                requested = PreferredNode.None;
                break;
            case NodeAuto:
                requested = PreferredNode.Auto;
                break;
            case NodeNamed:
                try
                {
                    requested = PreferredNode.For(PreferredNodeName);
                }
                catch (ArgumentException ex)
                {
                    errors.Add("Preferred node: " + ex.Message);
                    return;
                }

                break;
            default:
                errors.Add("Preferred node: '" + mode + "' is not a choice.");
                return;
        }

        // An automatic pin a node has claimed is still the automatic pin, so leaving the choice on auto is
        // leaving it alone rather than asking for the claim to be released.
        bool unchanged = mode == currentMode
            && (mode != NodeNamed || string.Equals(requested.Node, current.Node, StringComparison.Ordinal));
        if (unchanged)
        {
            return;
        }

        update.WithPreferredNode(requested);
        changes.Add(Changed("preferred node", Describe(current), Describe(requested)));
    }

    private void BuildOverlapPolicy(TriggerDetailsUpdate update, List<string> changes, List<string> errors)
    {
        if (OverlapPolicyUnavailable is not null)
        {
            return;
        }

        if (!Enum.TryParse(OverlapPolicy, ignoreCase: false, out OverlapPolicy policy) || !Enum.IsDefined(policy))
        {
            errors.Add("Overlap policy: '" + OverlapPolicy + "' is not an overlap policy.");
            return;
        }

        if (policy == original.OverlapPolicy)
        {
            return;
        }

        update.WithOverlapPolicy(policy);
        changes.Add(Changed("overlap policy", original.OverlapPolicy.ToString(), policy.ToString()));
    }

    private static List<MisfireOption> OptionsFor(ITrigger trigger)
    {
        return trigger switch
        {
            ISimpleTrigger => OptionsOf<SimpleTriggerMisfireInstruction>(),
            ICronTrigger => OptionsOf<CronTriggerMisfireInstruction>(),
            ICalendarIntervalTrigger => OptionsOf<CalendarIntervalTriggerMisfireInstruction>(),
            IDailyTimeIntervalTrigger => OptionsOf<DailyTimeIntervalTriggerMisfireInstruction>(),
            IRecurrenceTrigger => OptionsOf<RecurrenceTriggerMisfireInstruction>(),
            _ => []
        };
    }

    private static List<MisfireOption> OptionsOf<TInstruction>() where TInstruction : struct, Enum
    {
        List<MisfireOption> options = [];
        foreach (TInstruction value in Enum.GetValues<TInstruction>())
        {
            options.Add(new MisfireOption(Convert.ToInt32(value, CultureInfo.InvariantCulture), value.ToString()));
        }

        return options;
    }

    private static string ModeOf(PreferredNode node)
    {
        if (node.IsNone)
        {
            return NodeNone;
        }

        return node.IsAutomatic ? NodeAuto : NodeNamed;
    }

    private static string Describe(PreferredNode node)
    {
        if (node.IsNone)
        {
            return Nothing;
        }

        return node.IsAutomatic ? "auto" : node.Node ?? Nothing;
    }

    private static JobDataMap Copy(JobDataMap? source)
    {
        JobDataMap copy = new();
        if (source is not null)
        {
            foreach (KeyValuePair<string, object?> entry in source)
            {
                copy[entry.Key] = entry.Value;
            }
        }

        copy.ClearDirtyFlag();
        return copy;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Changed(string field, string? from, string? to)
    {
        return field + " " + (Blank(from) ?? Nothing) + " → " + (Blank(to) ?? Nothing);
    }
}

/// <summary>
/// One misfire instruction a trigger's family offers: the code it is stored as and the name it goes by.
/// </summary>
internal sealed record MisfireOption(int Code, string Name);

/// <summary>
/// What a press of <em>Save details</em> asks for.
/// </summary>
/// <param name="Update">The patch to send, or <see langword="null" /> when there is nothing valid to send.</param>
/// <param name="Changes">Each change, in the words the action log records it with.</param>
/// <param name="Errors">What the fields got wrong, shown beside them.</param>
internal sealed record TriggerDetailsEdit(TriggerDetailsUpdate? Update, List<string> Changes, List<string> Errors);
