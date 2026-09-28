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

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Quartz.Analyzers;

/// <summary>
/// What one <c>[QuartzJob]</c> class says, read once and carried through the generator's pipeline.
/// </summary>
/// <remarks>
/// Everything here is a value: a string, a number, or a list of values. Nothing holds a
/// <see cref="ISymbol" /> or a <see cref="Location" />, because an incremental generator compares its
/// steps' outputs to decide whether the next step has to run at all, and a symbol is neither
/// comparable nor safe to keep alive between compilations. <see cref="LocationInfo" /> is what stands
/// in for the location a diagnostic needs.
/// </remarks>
internal sealed record DeclaredJob(
    string TypeName,
    string DisplayName,
    JobProblem Problem,
    string Name,
    string? Group,
    string? Description,
    bool Durable,
    bool RequestRecovery,
    string? Scheduler,
    EquatableArray<DeclaredTrigger> Triggers,
    LocationInfo? Location);

/// <summary>
/// One <c>[CronTrigger]</c> or <c>[SimpleTrigger]</c> on a declared job.
/// </summary>
/// <param name="Schedule">
/// What the trigger fires on, which is the one thing the two attributes say differently.
/// </param>
/// <param name="Name">
/// The trigger key's name, already defaulted from the job's name and the trigger's position.
/// </param>
/// <param name="MisfireInstruction">
/// The misfire instruction's numeric value, so that <c>0</c> — <c>SmartPolicy</c> in both families,
/// the default — needs no call emitted for it.
/// </param>
/// <param name="MisfireInstructionName">
/// The enum member the value is, read off the enum rather than spelled here, or <see langword="null" />
/// for a value cast from a number that names no member.
/// </param>
/// <param name="ConfigurationKey">
/// The configuration key whose value replaces the schedule's expression or interval when it is set, or
/// <see langword="null" /> for a schedule that is the attribute's alone.
/// </param>
internal sealed record DeclaredTrigger(
    DeclaredSchedule Schedule,
    string Name,
    string? Group,
    int MisfireInstruction,
    string? MisfireInstructionName,
    int Priority,
    string? Description,
    string? ExecutionGroup,
    string? ConfigurationKey,
    LocationInfo? Location);

/// <summary>
/// What a declared trigger fires on.
/// </summary>
/// <remarks>
/// A record, like everything else in the model, so that the generator's incremental steps compare a
/// schedule by what it says.
/// </remarks>
internal abstract record DeclaredSchedule;

/// <summary>
/// A <c>[CronTrigger]</c>'s schedule.
/// </summary>
internal sealed record DeclaredCronSchedule(string CronExpression, string? TimeZone) : DeclaredSchedule;

/// <summary>
/// A <c>[SimpleTrigger]</c>'s schedule, its interval already parsed.
/// </summary>
/// <param name="IntervalTicks">
/// The interval as ticks, which is what the registration is written with, so nothing parses it at run
/// time.
/// </param>
/// <param name="IntervalText">
/// The interval in <see cref="TimeSpan" />'s invariant constant format, <c>00:10:00</c>, which the
/// generated file carries beside the ticks for its reader.
/// </param>
/// <param name="RepeatCount">
/// How many times the trigger repeats after its first firing, or <c>-1</c> for forever.
/// </param>
internal sealed record DeclaredSimpleSchedule(long IntervalTicks, string IntervalText, int RepeatCount) : DeclaredSchedule;

/// <summary>
/// A <c>[CronTrigger]</c> or <c>[SimpleTrigger]</c> on a class that declares no job.
/// </summary>
/// <param name="AttributeName">The attribute as it is written, <c>CronTrigger</c> or <c>SimpleTrigger</c>.</param>
internal sealed record OrphanTrigger(string DisplayName, string AttributeName, LocationInfo? Location);

/// <summary>
/// What the generated class and its methods are called in this assembly.
/// </summary>
/// <param name="AssemblyMethodName">
/// <c>AddDeclaredJobsFrom</c> and this assembly's name, which is emitted whatever else is: no other
/// assembly's registration can share it, so it binds wherever this one is visible, however many others
/// are visible beside it.
/// </param>
/// <param name="VisibleAssembly">
/// The assembly whose own generated class this compilation can already see through
/// <c>InternalsVisibleTo</c>, which is why this one is named after its assembly instead; or
/// <see langword="null" /> in the ordinary case, where it can see none and keeps the ordinary name.
/// </param>
internal sealed record RegistrationName(string ClassName, string AssemblyMethodName, string? VisibleAssembly)
{
    internal const string OrdinaryClassName = "QuartzDeclaredJobs";

    internal const string OrdinaryMethodName = "AddDeclaredJobs";

    /// <summary>
    /// Whether <c>AddDeclaredJobs</c> is emitted beside <see cref="AssemblyMethodName" />, which it is
    /// unless another assembly's is visible here and already means something.
    /// </summary>
    internal bool HasOrdinaryMethod => VisibleAssembly is null;
}

/// <summary>
/// Why a <c>[QuartzJob]</c> class cannot be registered, or <see cref="None" /> when it can.
/// </summary>
internal enum JobProblem
{
    /// <summary>The class is a job the generated registration can name.</summary>
    None,

    /// <summary>The class does not implement <c>IJob</c>, so <c>AddJob&lt;T&gt;</c> would not take it.</summary>
    NotAJob,

    /// <summary>The class is abstract, so nothing can construct it.</summary>
    Abstract,

    /// <summary>The class is generic, so a registration would have to pick its type arguments.</summary>
    Generic,

    /// <summary>The class cannot be named from another file in the same assembly.</summary>
    Inaccessible,
}

/// <summary>
/// Where a diagnostic goes, as values rather than as a <see cref="Location" />.
/// </summary>
/// <remarks>
/// A <see cref="Location" /> holds its syntax tree, which holds the compilation it came from; keeping
/// one in a generator's pipeline both defeats the comparison that makes the pipeline incremental and
/// roots a whole compilation in memory. These three values rebuild an equivalent location on demand.
/// </remarks>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    internal Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    /// <summary>
    /// The location of the syntax a reference points at, or <see langword="null" /> when it points at
    /// nothing in source — which is what an attribute read from metadata does.
    /// </summary>
    internal static LocationInfo? From(SyntaxReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        SyntaxNode node = reference.GetSyntax();
        return From(node.GetLocation());
    }

    internal static LocationInfo? From(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        return new LocationInfo(
            location.SourceTree!.FilePath,
            location.SourceSpan,
            location.GetLineSpan().Span);
    }
}

/// <summary>
/// An <see cref="ImmutableArray{T}" /> that compares as its contents do.
/// </summary>
/// <remarks>
/// A record holding an <see cref="ImmutableArray{T}" /> compares that field by reference, so two
/// runs of a generator's transform over the same unchanged source produce records that are not equal
/// — and an incremental generator that cannot tell "the same" from "changed" re-runs everything
/// downstream on every keystroke. This is the usual answer: one value type, element-wise equality.
/// </remarks>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> items;

    internal EquatableArray(ImmutableArray<T> items)
    {
        this.items = items;
    }

    public bool Equals(EquatableArray<T> other)
    {
        if (items.IsDefault || other.items.IsDefault)
        {
            return items.IsDefault && other.items.IsDefault;
        }

        if (items.Length != other.items.Length)
        {
            return false;
        }

        for (int i = 0; i < items.Length; i++)
        {
            if (!items[i].Equals(other.items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (items.IsDefault)
        {
            return 0;
        }

        int hash = 17;
        foreach (T item in items)
        {
            hash = (hash * 31) + (item?.GetHashCode() ?? 0);
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (items.IsDefault)
        {
            yield break;
        }

        foreach (T item in items)
        {
            yield return item;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
