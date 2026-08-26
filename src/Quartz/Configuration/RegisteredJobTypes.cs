using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Quartz.Configuration;

/// <summary>
/// The job types a container has been told to carry, and the scheduler each was named for.
/// </summary>
/// <remarks>
/// <para>
/// A job type the container holds is built by the <em>container</em> rather than activated by the job
/// factory, which is the difference <see cref="RegisteredJobConstructorValidator"/> exists to police.
/// Only what Quartz was told about is recorded: a type an application registered for reasons of its own
/// is its own business, even when it happens to implement <see cref="IJob"/>.
/// </para>
/// <para>
/// It keeps the service collection rather than a copy of what was registered, because how a job type is
/// built is only settled once registration is over. <c>AddJob</c> registers with <c>TryAdd</c> and loses
/// to a registration the application made itself, <c>AddJobType</c> replaces what came before it, and a
/// job built by a factory has no constructor for anything here to read — which is exactly the shape the
/// documentation recommends for a job that must be constructed with something of its scheduler's. Read
/// at registration time, each of those three would be read wrong.
/// </para>
/// </remarks>
internal sealed class RegisteredJobTypes
{
    private readonly IServiceCollection services;

    /// <summary>
    /// Which scheduler was told to carry which job type. A set, because the same job type can be named
    /// twice for one scheduler — <c>AddQuartz()</c> is additive, and container-wide configuration
    /// reaches every scheduler — and saying it twice is not two jobs.
    /// </summary>
    private readonly HashSet<(string SchedulerName, Type JobType)> registered = [];

    private RegisteredJobTypes(IServiceCollection services)
    {
        this.services = services;
    }

    /// <summary>
    /// Returns the record belonging to a service collection, registering one — and the validator that
    /// reads it — on first use.
    /// </summary>
    /// <remarks>
    /// Held as a registered instance for the reason <see cref="SchedulerNameRegistry"/> is: it is written
    /// while registration is still going on. The validator is registered here rather than beside the
    /// other options validators so that it exists only in a container that was actually given a job to
    /// carry, and so that it can require this record rather than do without it.
    /// </remarks>
    public static RegisteredJobTypes For(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(RegisteredJobTypes)
                && descriptor.ImplementationInstance is RegisteredJobTypes existing)
            {
                return existing;
            }
        }

        RegisteredJobTypes registrations = new(services);
        services.AddSingleton(registrations);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<QuartzSchedulerOptions>,
            RegisteredJobConstructorValidator>());

        return registrations;
    }

    /// <summary>
    /// Records that a scheduler was given a job of this type.
    /// </summary>
    /// <param name="schedulerName">
    /// The scheduler the job was added to, empty or <see langword="null"/> for the default one.
    /// </param>
    /// <param name="jobType">The job type, as the container knows it.</param>
    public void Add(string? schedulerName, Type jobType)
    {
        registered.Add((schedulerName ?? Options.DefaultName, jobType));
    }

    /// <summary>
    /// The registrations one scheduler's job types would be built from, in no particular order.
    /// </summary>
    public List<ServiceDescriptor> Registrations(string schedulerName)
    {
        List<ServiceDescriptor> found = [];

        // The default scheduler's registrations are the unkeyed ones; a key of "" is not the same as no
        // key to a container.
        object? key = string.IsNullOrEmpty(schedulerName) ? null : schedulerName;

        foreach ((string name, Type jobType) in registered)
        {
            if (!string.Equals(name, schedulerName, StringComparison.Ordinal))
            {
                continue;
            }

            if (Winner(jobType, key) is { } descriptor)
            {
                found.Add(descriptor);
            }
        }

        return found;
    }

    /// <summary>
    /// The registration the job factory's resolution lands on: this scheduler's own, then the container's
    /// unkeyed one, the last registration winning in each case — which is how the container resolves.
    /// </summary>
    private ServiceDescriptor? Winner(Type jobType, object? key)
    {
        return Last(jobType, key) ?? (key is null ? null : Last(jobType, serviceKey: null));
    }

    private ServiceDescriptor? Last(Type jobType, object? serviceKey)
    {
        ServiceDescriptor? found = null;

        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == jobType && Equals(descriptor.ServiceKey, serviceKey))
            {
                found = descriptor;
            }
        }

        return found;
    }

    /// <summary>
    /// The type the container would construct for a registration, or <see langword="null"/> when it
    /// constructs nothing — a factory of the application's own, or an instance it handed over ready
    /// made.
    /// </summary>
    /// <remarks>
    /// The keyed and unkeyed properties are separate members of <see cref="ServiceDescriptor"/>, and
    /// reading the wrong one throws rather than returning <see langword="null"/>. Both carry the
    /// annotation that makes reading the type's constructors trimmable, which is why the type is taken
    /// from here rather than from what was recorded above.
    /// </remarks>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static Type? ImplementationType(ServiceDescriptor descriptor)
    {
        return descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;
    }
}
