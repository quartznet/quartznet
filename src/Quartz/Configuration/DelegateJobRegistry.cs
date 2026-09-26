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

using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// The delegate jobs a container has been told to carry: which handler runs when a scheduler fires a
/// given key.
/// </summary>
/// <remarks>
/// <para>
/// Two tables, filled at two moments. What each scheduler was <em>told</em> is recorded as the
/// registration is written, under the scheduler's options name, because that is when
/// <see cref="RegisteredJobConstructorValidator" /> needs it: validation runs before any scheduler
/// exists. Which key runs which handler is only settled once the scheduler's content is built — a
/// job's configuration callback may give it an identity of its own, and <c>ScheduleJob</c>'s job takes
/// its trigger's — so that table is filled then, under the name the scheduler runs as, which is the name
/// a firing can read off <see cref="IJobExecutionContext.Scheduler" />.
/// </para>
/// <para>
/// Held as a registered instance for the reason <see cref="RegisteredJobTypes" /> is: it is written while
/// registration is still going on. The firing table is concurrent because schedulers in one container
/// can be built at once, and every firing of every delegate job reads it.
/// </para>
/// </remarks>
internal sealed class DelegateJobRegistry
{
    private readonly List<(string SchedulerName, string Name, DelegateJobBinding Binding)> declared = [];
    private readonly ConcurrentDictionary<(string SchedulerName, JobKey Key), DelegateJobBinding> bound = new();

    private DelegateJobRegistry()
    {
    }

    /// <summary>
    /// Returns the registry belonging to a service collection, registering one on first use.
    /// </summary>
    public static DelegateJobRegistry For(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(DelegateJobRegistry)
                && descriptor.ImplementationInstance is DelegateJobRegistry existing)
            {
                return existing;
            }
        }

        DelegateJobRegistry registry = new();
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>
    /// Records that a scheduler was told to carry a delegate job.
    /// </summary>
    /// <param name="schedulerName">
    /// The scheduler's options name, empty or <see langword="null" /> for the default one.
    /// </param>
    /// <param name="name">The name the job was added under.</param>
    /// <param name="binding">The handler.</param>
    public void Declare(string? schedulerName, string name, DelegateJobBinding binding)
    {
        declared.Add((schedulerName ?? Options.DefaultName, name, binding));
    }

    /// <summary>
    /// The delegate jobs one scheduler was told to carry, by the options name it was registered under.
    /// </summary>
    public List<(string Name, DelegateJobBinding Binding)> Declared(string schedulerName)
    {
        List<(string Name, DelegateJobBinding Binding)> found = [];
        foreach ((string scheduler, string name, DelegateJobBinding binding) in declared)
        {
            if (string.Equals(scheduler, schedulerName, StringComparison.Ordinal))
            {
                found.Add((name, binding));
            }
        }

        return found;
    }

    /// <summary>
    /// Records which handler a scheduler runs for a key, once the job's identity is settled.
    /// </summary>
    /// <remarks>
    /// Binding the same handler twice is harmless: two containers built from one service collection
    /// build the same content. Two <em>different</em> handlers under one key on one scheduler are
    /// refused, because a firing could not say which of them it meant.
    /// </remarks>
    /// <param name="schedulerName">The name the scheduler runs as.</param>
    /// <param name="key">The job's key.</param>
    /// <param name="binding">The handler.</param>
    /// <exception cref="InvalidOperationException">Another handler is already bound to the key.</exception>
    public void Bind(string schedulerName, JobKey key, DelegateJobBinding binding)
    {
        DelegateJobBinding existing = bound.GetOrAdd((schedulerName, key), binding);
        if (!ReferenceEquals(existing, binding))
        {
            Throw.InvalidOperationException(
                $"Two delegate jobs on scheduler '{schedulerName}' are keyed '{key}'. A delegate job's key is what "
                + "a firing finds its handler by, so it has to be unique on its scheduler: give one of them another "
                + "name, or another identity in its configuration callback.");
        }
    }

    /// <summary>
    /// The handler a scheduler runs for a key, or <see langword="null" /> when it was given none.
    /// </summary>
    /// <param name="schedulerName">The name the scheduler runs as.</param>
    /// <param name="key">The job's key.</param>
    public DelegateJobBinding? Find(string schedulerName, JobKey key)
    {
        return bound.GetValueOrDefault((schedulerName, key));
    }
}
