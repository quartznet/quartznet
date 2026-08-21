using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Configuration;

/// <summary>
/// The names <c>AddQuartz(name, …)</c> has been called with, so that a second registration under a name
/// already taken is refused where it is written rather than where the scheduler is bound.
/// </summary>
/// <remarks>
/// Names are compared case-insensitively, because <see cref="Quartz.Impl.SchedulerRepository"/> indexes
/// them that way: two registrations this list called distinct would collide there, and the report would
/// arrive at host start instead of at the call that caused it.
/// </remarks>
internal sealed class SchedulerNameRegistry
{
    private readonly List<string> names = [];

    public IReadOnlyList<string> Names => names;

    /// <summary>
    /// Returns the registry belonging to a service collection, registering one on first use.
    /// </summary>
    /// <remarks>
    /// It is held as a registered instance rather than resolved from the built container, because the
    /// names have to be known while registration is still going on — that is the whole point of catching
    /// the duplicate there. Registering it unconditionally is what lets the options validator require it.
    /// </remarks>
    public static SchedulerNameRegistry For(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType == typeof(SchedulerNameRegistry)
                && descriptor.ImplementationInstance is SchedulerNameRegistry existing)
            {
                return existing;
            }
        }

        SchedulerNameRegistry registry = new();
        services.AddSingleton(registry);
        return registry;
    }

    public void Add(string name)
    {
        if (Find(name) is not null)
        {
            throw new ArgumentException($"A scheduler with name '{name}' has already been registered.", nameof(name));
        }

        names.Add(name);
    }

    /// <summary>
    /// Returns the registered name matching <paramref name="name"/>, as it was spelled at its
    /// registration, or <see langword="null"/> when no scheduler was registered under it.
    /// </summary>
    public string? Find(string? name)
    {
        foreach (string registered in names)
        {
            if (string.Equals(registered, name, StringComparison.OrdinalIgnoreCase))
            {
                return registered;
            }
        }

        return null;
    }
}
