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

using System.Diagnostics.CodeAnalysis;

namespace Quartz;

/// <summary>
/// Puts an <see cref="IJob{TInput}" />'s input on the job or the trigger being built.
/// </summary>
/// <remarks>
/// <para>
/// Extensions rather than members, because a method cannot constrain its own type's type parameter:
/// <c>JobBuilder&lt;TJob&gt;</c> is declared for any <c>TJob : IJob</c>, and a member saying
/// <c>where TJob : IJob&lt;TInput&gt;</c> is CS0699. Written here, the constraint is the method's own
/// and the compiler can check it — so <c>UsingInput</c> is offered on a builder for a typed job and
/// refused on a builder for a job that takes no input.
/// </para>
/// <para>
/// The input type is inferred from the argument's <em>static</em> type. A payload held as a base type —
/// or as <see cref="object" /> — is stored and read back as that type unless the type argument is given
/// explicitly: <c>builder.UsingInput&lt;SendEmailJob, SendEmail&gt;(payload)</c>.
/// </para>
/// <para>
/// The value goes into the map as it is; the scheduler serializes it as the job or trigger is stored,
/// which is the only point at which a serializer exists. So a builder needs none, and a job data map
/// built here can still be inspected before it is scheduled.
/// </para>
/// </remarks>
/// <seealso cref="IJob{TInput}" />
/// <seealso cref="SchedulerConstants.JobInput" />
public static class JobInputBuilderExtensions
{
    /// <summary>
    /// Sets the input the job carries.
    /// </summary>
    /// <remarks>
    /// An input on the job is the default for every one of its triggers, and any trigger can override
    /// it. Note that a <see cref="PersistJobDataAfterExecutionAttribute" /> job re-stores its own map
    /// after every firing, so an input meant to differ per firing belongs on the trigger.
    /// </remarks>
    /// <param name="builder">The job being built.</param>
    /// <param name="input">The payload the job runs with.</param>
    public static JobBuilder<TJob> UsingInput<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        this JobBuilder<TJob> builder,
        TInput input) where TJob : IJob<TInput>
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UsingJobData(SchedulerConstants.JobInput, input);
    }

    /// <summary>
    /// Sets the input this trigger's firings carry, overriding any input on the job.
    /// </summary>
    /// <param name="builder">The trigger being built.</param>
    /// <param name="input">The payload the job runs with when this trigger fires it.</param>
    public static TriggerBuilder<TJob> UsingInput<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        this TriggerBuilder<TJob> builder,
        TInput input) where TJob : IJob<TInput>
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UsingJobData(SchedulerConstants.JobInput, input);
    }

    /// <inheritdoc cref="UsingInput{TJob, TInput}(JobBuilder{TJob}, TInput)" />
    /// <param name="configurator">The job being configured.</param>
    /// <param name="input">The payload the job runs with.</param>
    public static IJobConfigurator<TJob> UsingInput<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        this IJobConfigurator<TJob> configurator,
        TInput input) where TJob : IJob<TInput>
    {
        ArgumentNullException.ThrowIfNull(configurator);

        return configurator.UsingJobData(SchedulerConstants.JobInput, input);
    }

    /// <inheritdoc cref="UsingInput{TJob, TInput}(TriggerBuilder{TJob}, TInput)" />
    /// <param name="configurator">The trigger being configured.</param>
    /// <param name="input">The payload the job runs with when this trigger fires it.</param>
    public static ITriggerConfigurator<TJob> UsingInput<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        this ITriggerConfigurator<TJob> configurator,
        TInput input) where TJob : IJob<TInput>
    {
        ArgumentNullException.ThrowIfNull(configurator);

        return configurator.UsingJobData(SchedulerConstants.JobInput, input);
    }
}
