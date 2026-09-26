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

namespace Quartz;

/// <summary>
/// The execution group a trigger for this job counts against when it names none of its own, such as
/// <c>[ExecutionGroup("tenant:{TenantId}")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Read by <see cref="TriggerBuilder{TJob}" /> for its own <c>TJob</c>, and by the one-call
/// <c>ScheduleJob&lt;TJob, TInput&gt;</c> overloads. An explicit <c>WithExecutionGroup</c> — or
/// <see cref="OneOffJobOptions.ExecutionGroup" /> — wins, and <c>WithExecutionGroup(null)</c> opts one
/// trigger out. A trigger built for <see cref="IJob" />, or built before this attribute was added, is not
/// affected: the group is resolved once, when the trigger is built, and stored as a plain name.
/// </para>
/// <para>
/// <c>{key}</c> placeholders read the trigger's own <see cref="JobDataMap" /> when the trigger is built.
/// <c>{{</c> and <c>}}</c> are literal braces. A placeholder with nothing behind it fails the build,
/// naming the key.
/// </para>
/// <para>
/// The one-call overloads apply a name with no placeholders. A one-off trigger's job data is its input,
/// stored whole, so a template there has nothing to read and the call throws
/// <see cref="FormatException" />: name the group at the call site instead, as in
/// <c>OneOffJobOptions.ExecutionGroup = $"tenant:{input.TenantId}"</c>.
/// </para>
/// <para>
/// Inherited from a base class. Unlike <see cref="DisallowConcurrentExecutionAttribute" /> it is not read
/// from interfaces, because a group is a property of the concrete work, and two contracts could name two.
/// </para>
/// </remarks>
/// <seealso cref="ExecutionLimitsBuilder.ForGroupsWithPrefix" />
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ExecutionGroupAttribute : Attribute
{
    /// <summary>
    /// Declares the execution group, or a template for one.
    /// </summary>
    /// <param name="template">A group name, or one with <c>{key}</c> placeholders.</param>
    /// <exception cref="ArgumentNullException"><paramref name="template" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="template" /> is blank, malformed, or a name
    /// reserved for limits configuration.</exception>
    public ExecutionGroupAttribute(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        string trimmed = template.Trim();

        if (trimmed.Length == 0)
        {
            Throw.ArgumentException("An execution group needs a name; leave the attribute off for none.", nameof(template));
        }

        if (ExecutionGroupTemplate.IsTemplate(trimmed))
        {
            ExecutionGroupTemplate.Validate(trimmed, nameof(template));
        }
        else if (ExecutionLimits.IsReservedGroupName(trimmed))
        {
            Throw.ArgumentException($"Execution group name '{trimmed}' is reserved for limits configuration.", nameof(template));
        }

        Template = trimmed;
    }

    /// <summary>
    /// The execution group, or the template it is resolved from.
    /// </summary>
    public string Template { get; }
}

/// <summary>
/// What <see cref="ExecutionGroupAttribute" /> says about one job type, read once.
/// </summary>
/// <remarks>
/// Not a static initializer: an attribute whose constructor refuses its argument would turn into a
/// <see cref="TypeInitializationException" /> for every later build. Read again until it succeeds; the
/// race is a second read of the same metadata.
/// </remarks>
internal static class DeclaredExecutionGroup<TJob>
{
    private static string? template;
    private static volatile bool read;

    internal static string? Template
    {
        get
        {
            if (!read)
            {
                template = typeof(TJob).GetCustomAttribute<ExecutionGroupAttribute>(inherit: true)?.Template;
                read = true;
            }

            return template;
        }
    }
}
