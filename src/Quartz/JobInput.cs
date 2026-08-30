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

namespace Quartz;

/// <summary>
/// Reads the input a job was scheduled with.
/// </summary>
/// <remarks>
/// An extension rather than a member of <see cref="IJobExecutionContext" />, so that every hand-written
/// context — a test double, an adapter — keeps compiling. An <see cref="IJob{TInput}" /> does not need
/// this: its input arrives as a parameter.
/// </remarks>
public static class JobExecutionContextInputExtensions
{
    /// <summary>
    /// The input this firing carries, or <see langword="default" /> when it carries none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trigger's input wins over the job's, which is what <see cref="IJobExecutionContext.MergedJobDataMap" />
    /// means everywhere else.
    /// </para>
    /// <para>
    /// <typeparamref name="TInput" /> is the type the payload is read back as, so a job reading its own
    /// input names the same type the scheduling side passed. An input stored as JSON by a different type
    /// comes back as whatever this type can make of that JSON.
    /// </para>
    /// </remarks>
    /// <typeparam name="TInput">The type the input was scheduled as.</typeparam>
    /// <param name="context">The firing to read the input of.</param>
    /// <exception cref="SchedulerException">
    /// The input is stored but cannot be read: the context was built without an
    /// <see cref="IJobInputSerializer" />, or the stored value is neither a payload nor a
    /// <typeparamref name="TInput" />.
    /// </exception>
    public static TInput? GetInput<TInput>(this IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return JobInput.TryRead(context, out TInput? input) ? input : default;
    }
}

/// <summary>
/// The two halves of a typed job's input round trip: normalizing it to a string on the way into a
/// store, and reading it back on the way out.
/// </summary>
/// <remarks>
/// They are apart in the code because they are apart in time and in what they know. The scheduler knows
/// the value's <em>runtime</em> type, which is what has to be written; the job knows its
/// <em>static</em> type, which is what has to be read. Neither knows the other's.
/// </remarks>
internal static class JobInput
{
    /// <summary>
    /// Replaces a job input that is not already a string with its serialized form, so that whatever the
    /// map goes on to travel through carries it unchanged.
    /// </summary>
    /// <remarks>
    /// A value that is already a string is left exactly as it is. That is what makes a
    /// <see cref="string" /> payload work, and it is what makes normalizing a map twice — a job data map
    /// handed to a second <c>ScheduleJob</c>, for instance — do nothing the second time.
    /// </remarks>
    public static void Normalize(JobDataMap? map, IJobInputSerializer serializer)
    {
        if (map is null || !map.TryGetValue(SchedulerConstants.JobInput, out object? value) || value is null or string)
        {
            return;
        }

        map[SchedulerConstants.JobInput] = serializer.Serialize(value);
    }

    /// <summary>
    /// Reads the input an <see cref="IJob{TInput}" /> declared, which it is an error not to have.
    /// </summary>
    public static TInput Read<TInput>(IJobExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryRead(context, out TInput? input))
        {
            Throw.SchedulerException(
                $"Job '{context.JobDetail.Key}' takes an input of type {typeof(TInput)}, but neither its job data map nor "
                + $"trigger '{context.Trigger.Key}' carries one under '{SchedulerConstants.JobInput}'. "
                + "Set it with UsingInput() on the job or trigger builder.");
        }

        return input!;
    }

    /// <summary>
    /// Reads the input this firing carries, reporting whether it carried one at all.
    /// </summary>
    public static bool TryRead<TInput>(IJobExecutionContext context, out TInput? input)
    {
        if (!context.MergedJobDataMap.TryGetValue(SchedulerConstants.JobInput, out object? stored) || stored is null)
        {
            input = default;
            return false;
        }

        // A value of the asked-for type is handed straight back. That is the case for a string payload,
        // which is stored verbatim, and for a map that never went through a store at all.
        if (stored is TInput typed)
        {
            input = typed;
            return true;
        }

        if (stored is string payload)
        {
            input = Serializer(context).Deserialize<TInput>(payload);
            return true;
        }

        Throw.SchedulerException(
            $"Job '{context.JobDetail.Key}' takes an input of type {typeof(TInput)}, but '{SchedulerConstants.JobInput}' "
            + $"holds a {stored.GetType()}. An input is stored either as itself or as the string an IJobInputSerializer wrote.");
        input = default;
        return false;
    }

    /// <summary>
    /// The serializer the context was built with.
    /// </summary>
    /// <remarks>
    /// A context Quartz built has one. A context built by hand — a test double, an adapter — may not,
    /// and is told so by name rather than left to reflect its way to an answer.
    /// </remarks>
    private static IJobInputSerializer Serializer(IJobExecutionContext context)
    {
        if (context is IJobInputSource { JobInputSerializer: { } serializer })
        {
            return serializer;
        }

        Throw.SchedulerException(
            $"The execution context for job '{context.JobDetail.Key}' was built without an {nameof(IJobInputSerializer)}, "
            + "so a stored job input cannot be read back. A context Quartz creates carries the scheduler's serializer; "
            + $"one built by hand has to be given it through the {nameof(Impl.JobExecutionContextImpl)} constructor.");
        return null!;
    }
}

/// <summary>
/// An execution context that knows how to read a stored job input.
/// </summary>
/// <remarks>
/// Internal, and deliberately not a member of <see cref="IJobExecutionContext" />: the serializer is a
/// scheduler's own part, not something a context's implementer should have to supply.
/// </remarks>
internal interface IJobInputSource
{
    /// <summary>
    /// The serializer this firing's inputs are read with, or <see langword="null" /> when the context was
    /// built without one.
    /// </summary>
    IJobInputSerializer? JobInputSerializer { get; }
}
