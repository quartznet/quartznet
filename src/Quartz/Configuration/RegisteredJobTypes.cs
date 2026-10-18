using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

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
/// <para>
/// Whether <c>AddJob</c> registers the job type at all is settled late for the same reason. Only a
/// scheduler whose job factory builds jobs from the container gets the registration, and
/// <c>UseJobFactory</c> may be written after <c>AddJob</c> in the same callback, so the registration
/// waits in <see cref="Request" /> until the scheduler's registration is complete. A factory chosen even
/// later — by <c>ConfigureAllQuartzSchedulers</c> after <c>AddQuartz</c> — replaces the default the
/// decision was made against, and <see cref="Reconsider" /> makes it again.
/// </para>
/// </remarks>
internal sealed class RegisteredJobTypes
{
    private readonly IServiceCollection services;

    /// <summary>
    /// Which scheduler <c>AddJobType</c> told to carry which job type, whatever its job factory. A set,
    /// because the same job type can be named twice for one scheduler — <c>AddQuartz()</c> is additive,
    /// and container-wide configuration reaches every scheduler — and saying it twice is not two jobs.
    /// </summary>
    private readonly HashSet<(string SchedulerName, Type JobType)> declared = [];

    /// <summary>
    /// The job types <see cref="RegisterRequested" /> registered for a scheduler, one entry per scheduler
    /// and type, with the registration it added on that scheduler's behalf — or <see langword="null" />
    /// when one was already there, the application's or another scheduler's.
    /// </summary>
    /// <remarks>
    /// The registration is unkeyed, so schedulers share it, and which of them added it decides nothing
    /// but who hands it on: <see cref="Reconsider" /> removes it only when no scheduler still on a
    /// container-backed factory asked for the same type.
    /// </remarks>
    private readonly List<(string SchedulerName, Type JobType, ServiceDescriptor? Added)> granted = [];

    /// <summary>
    /// The registrations <c>AddJob</c> and <c>ScheduleJob</c> asked for, waiting for the job factory of
    /// the scheduler they were asked for to be known.
    /// </summary>
    private readonly List<(string SchedulerName, ServiceDescriptor Registration)> requested = [];

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
        if (Find(services) is { } existing)
        {
            return existing;
        }

        RegisteredJobTypes registrations = new(services);
        services.AddSingleton(registrations);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<QuartzSchedulerOptions>,
            RegisteredJobConstructorValidator>());

        return registrations;
    }

    /// <summary>
    /// Returns the record belonging to a service collection, or <see langword="null"/> when nothing has
    /// given it a job yet.
    /// </summary>
    public static RegisteredJobTypes? Find(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(RegisteredJobTypes)
                && descriptor.ImplementationInstance is RegisteredJobTypes existing)
            {
                return existing;
            }
        }

        return null;
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
        declared.Add((schedulerName ?? Options.DefaultName, jobType));
    }

    /// <summary>
    /// Asks for a job type to be registered with the container for a scheduler, once that scheduler's job
    /// factory is known.
    /// </summary>
    /// <param name="schedulerName">
    /// The scheduler the job was added to, empty or <see langword="null"/> for the default one.
    /// </param>
    /// <param name="registration">
    /// The registration to add, built where the job type's constructors are known to be kept.
    /// </param>
    public void Request(string? schedulerName, ServiceDescriptor registration)
    {
        requested.Add((schedulerName ?? Options.DefaultName, registration));
    }

    /// <summary>
    /// Registers what a scheduler asked for, if its job factory builds jobs from the container, and
    /// records the job types for <see cref="RegisteredJobConstructorValidator"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once the scheduler's registration is complete, which is when its job factory can be read:
    /// Quartz registers its default factory last, so everything the scheduler chose for itself is in by
    /// then. A factory of the application's own gets neither the registration nor the check. It builds
    /// its jobs its own way, so the container never constructs them for this scheduler.
    /// </para>
    /// <para>
    /// The <c>TryAdd</c> keeps a registration the application made itself, and a job type that a
    /// scheduler on a container-backed factory asked for stays registered whatever another scheduler's
    /// factory is — including when <see cref="Reconsider" /> takes back what this added.
    /// </para>
    /// </remarks>
    /// <param name="schedulerName">The scheduler, <see langword="null"/> or empty for the default one.</param>
    public void RegisterRequested(string? schedulerName)
    {
        string name = schedulerName ?? Options.DefaultName;

        if (BuildsJobsFromContainer(name))
        {
            foreach ((string requestedFor, ServiceDescriptor registration) in requested)
            {
                if (string.Equals(requestedFor, name, StringComparison.Ordinal))
                {
                    int before = services.Count;
                    services.TryAdd(registration);
                    Grant(name, registration.ServiceType, services.Count > before ? registration : null);
                }
            }
        }

        requested.RemoveAll(request => string.Equals(request.SchedulerName, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Decides again whether a scheduler's job types are registered, now that a job factory chosen after
    /// its registration replaced the default the first decision was made against.
    /// </summary>
    /// <remarks>
    /// The default builds jobs from the container, so the first decision registered everything the
    /// scheduler asked for. A factory that builds them its own way takes that back — exactly what the
    /// scheduler would have had if the factory had been chosen before its registration completed, which is
    /// the point: <c>ConfigureAllQuartzSchedulers(q =&gt; q.UseJobFactory&lt;T&gt;())</c> means the same
    /// thing on either side of <c>AddQuartz</c>. A registration another scheduler still relies on is handed
    /// to it rather than removed.
    /// </remarks>
    /// <param name="schedulerName">The scheduler, <see langword="null"/> or empty for the default one.</param>
    public void Reconsider(string? schedulerName)
    {
        string name = schedulerName ?? Options.DefaultName;

        if (BuildsJobsFromContainer(name))
        {
            return;
        }

        for (int i = granted.Count - 1; i >= 0; i--)
        {
            (string grantedTo, Type jobType, ServiceDescriptor? added) = granted[i];
            if (!string.Equals(grantedTo, name, StringComparison.Ordinal))
            {
                continue;
            }

            granted.RemoveAt(i);

            if (added is not null && !HandOver(jobType, added))
            {
                services.Remove(added);
            }
        }
    }

    /// <summary>
    /// Records that a scheduler's job type is registered, once per scheduler and type.
    /// </summary>
    private void Grant(string schedulerName, Type jobType, ServiceDescriptor? added)
    {
        foreach ((string grantedTo, Type grantedType, _) in granted)
        {
            if (string.Equals(grantedTo, schedulerName, StringComparison.Ordinal) && grantedType == jobType)
            {
                return;
            }
        }

        granted.Add((schedulerName, jobType, added));
    }

    /// <summary>
    /// Gives a registration to another scheduler that asked for the same job type, if there is one.
    /// </summary>
    /// <remarks>
    /// Every scheduler still holding a grant is on a container-backed factory, since
    /// <see cref="Reconsider" /> takes a scheduler's grants away the moment its factory stops being one.
    /// </remarks>
    private bool HandOver(Type jobType, ServiceDescriptor registration)
    {
        for (int i = 0; i < granted.Count; i++)
        {
            (string grantedTo, Type grantedType, ServiceDescriptor? added) = granted[i];
            if (grantedType == jobType && added is null)
            {
                granted[i] = (grantedTo, grantedType, registration);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the job factory the container resolves for a scheduler — the last registration — builds
    /// jobs from the container.
    /// </summary>
    private bool BuildsJobsFromContainer(string schedulerName)
    {
        object? key = string.IsNullOrEmpty(schedulerName) ? null : schedulerName;
        return Last(typeof(IJobFactory), key) is { } jobFactory && JobFactoryDescriptor.BuildsJobsFromContainer(jobFactory);
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

        HashSet<Type> jobTypes = [];
        foreach ((string name, Type jobType) in declared)
        {
            if (string.Equals(name, schedulerName, StringComparison.Ordinal))
            {
                jobTypes.Add(jobType);
            }
        }

        foreach ((string name, Type jobType, _) in granted)
        {
            if (string.Equals(name, schedulerName, StringComparison.Ordinal))
            {
                jobTypes.Add(jobType);
            }
        }

        foreach (Type jobType in jobTypes)
        {
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
