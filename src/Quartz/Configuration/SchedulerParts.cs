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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// Registers a scheduler's parts so that a default Quartz filled in never beats a part somebody chose,
/// whichever of the two was registered first.
/// </summary>
/// <remarks>
/// <para>
/// Registration is first-wins, and a scheduler's registration fills in its defaults last, so a part
/// chosen before that — in its callback, by a key, by <c>ConfigureAllQuartzSchedulers</c> — is never
/// beaten to the slot. A part chosen <em>after</em> it is the case first-wins gets wrong:
/// <c>ConfigureAllQuartzSchedulers</c> reaches a scheduler that is already registered after its defaults
/// went in, and so does a second <c>AddQuartz()</c> for the default scheduler. A plain <c>TryAdd</c> then
/// lost to the default it was meant to replace, and nothing said so (#4104).
/// </para>
/// <para>
/// So the defaults are recorded as they go in, in <see cref="SchedulerNameRegistry" />, and a choice
/// replaces one in place. Anything else in the slot — a part the scheduler chose, a registration the
/// application made — still wins: first-wins holds between choices and only stops applying to a default.
/// </para>
/// <para>
/// Recorded rather than deferred. Holding the defaults back until every contribution is in would need a
/// moment after the last one, and there is none: <c>ConfigureAllQuartzSchedulers</c> may be the last
/// call before the container is built, and nothing of Quartz's runs then.
/// </para>
/// </remarks>
internal static class SchedulerParts
{
    /// <summary>
    /// Registers a default, unless the slot already holds something, and records it as a default.
    /// </summary>
    public static void TryAddDefault(IServiceCollection services, ServiceDescriptor descriptor)
    {
        SchedulerNameRegistry registry = SchedulerNameRegistry.For(services);

        int before = services.Count;
        services.TryAdd(descriptor);

        if (services.Count > before)
        {
            registry.RecordDefaultPart(descriptor);
        }
    }

    /// <summary>
    /// Registers a part somebody chose: into an empty slot, or in place of a default. A slot holding
    /// anything else keeps it.
    /// </summary>
    /// <returns>Whether this registration is the one the container will resolve.</returns>
    public static bool TryAdd(IServiceCollection services, ServiceDescriptor descriptor)
    {
        SchedulerNameRegistry? registry = SchedulerNameRegistry.Existing(services);
        int replaced = -1;

        for (int i = 0; i < services.Count; i++)
        {
            ServiceDescriptor existing = services[i];
            if (!SameSlot(existing, descriptor))
            {
                continue;
            }

            if (replaced >= 0 || registry is null || !registry.IsDefaultPart(existing))
            {
                return false;
            }

            replaced = i;
        }

        if (replaced < 0)
        {
            services.Add(descriptor);
            return true;
        }

        // In place, so the order of everything else in the collection is the order it was written in.
        registry!.ForgetDefaultPart(services[replaced]);
        services[replaced] = descriptor;

        // A scheduler's job factory decides whether its job types are registered with the container, and
        // the default one has decided already.
        if (descriptor.ServiceType == typeof(IJobFactory))
        {
            RegisteredJobTypes.Find(services)?.Reconsider(descriptor.ServiceKey as string);
        }

        return true;
    }

    /// <summary>
    /// Whether a scheduler's slot holds a part somebody chose, rather than nothing or a default.
    /// </summary>
    /// <param name="services">The collection the scheduler is registered into.</param>
    /// <param name="serviceType">The part.</param>
    /// <param name="schedulerKey">The scheduler's service key, or <see langword="null" /> for the default scheduler.</param>
    public static bool IsChosen(IServiceCollection services, Type serviceType, object? schedulerKey)
    {
        SchedulerNameRegistry? registry = SchedulerNameRegistry.Existing(services);

        foreach (ServiceDescriptor existing in services)
        {
            if (existing.ServiceType == serviceType
                && Equals(existing.ServiceKey, schedulerKey)
                && registry?.IsDefaultPart(existing) != true)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether two registrations are for the same scheduler's same part, as <c>TryAdd</c> compares them.
    /// </summary>
    private static bool SameSlot(ServiceDescriptor existing, ServiceDescriptor descriptor)
    {
        return existing.ServiceType == descriptor.ServiceType && Equals(existing.ServiceKey, descriptor.ServiceKey);
    }
}
