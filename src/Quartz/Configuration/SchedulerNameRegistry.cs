using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Configuration;

/// <summary>
/// What the container has been told to register a scheduler under, while it is still being told: the
/// names <c>AddQuartz(name, …)</c> has been called with, so that a second registration under a name
/// already taken is refused where it is written rather than where the scheduler is bound, and the
/// container-wide configuration <c>ConfigureAllQuartzSchedulers</c> has recorded.
/// </summary>
/// <remarks>
/// Names are compared case-insensitively, because <see cref="Quartz.Impl.SchedulerRepository"/> indexes
/// them that way: two registrations this list called distinct would collide there, and the report would
/// arrive at host start instead of at the call that caused it.
/// </remarks>
internal sealed class SchedulerNameRegistry
{
    private readonly List<string> names = [];

    /// <summary>
    /// The delegates <c>ConfigureAllQuartzSchedulers</c> has recorded, in the order they were recorded.
    /// </summary>
    private readonly List<Action<IQuartzBuilder>> configureAll = [];

    /// <summary>
    /// Which of those delegates has already been applied to which scheduler.
    /// </summary>
    /// <remarks>
    /// Registering the default scheduler is additive, so <c>AddQuartz()</c> can be called twice for one
    /// scheduler — and a delegate that adds a listener would then add it twice, since a listener
    /// registration is not a <c>TryAdd</c>. A delegate reaches each scheduler once, whichever call
    /// carries it there.
    /// </remarks>
    private readonly HashSet<(Action<IQuartzBuilder> Configure, string? SchedulerName)> applied = [];

    public IReadOnlyList<string> Names => names;

    /// <summary>
    /// Whether a default scheduler is registered — <c>AddQuartz()</c> without a name, or
    /// <c>AddQuartzScheduler()</c> directly, which is also how the standalone builder registers its one
    /// scheduler.
    /// </summary>
    /// <remarks>
    /// The default scheduler is not in <see cref="Names"/> and cannot be: it has no service key, and its
    /// name is whatever its options end up saying rather than something the call site chose. So the flag
    /// records that it exists, and its name is read from its options by whoever needs it — which is how
    /// <see cref="ISchedulerRegistry"/> can list it beside the named ones.
    /// </remarks>
    public bool HasDefaultScheduler { get; private set; }

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
    /// Records that the default scheduler is registered.
    /// </summary>
    /// <remarks>
    /// Deliberately not a duplicate check, unlike <see cref="Add"/>: registering the default scheduler is
    /// additive — calling <c>AddQuartz()</c> twice contributes two sets of configuration to one scheduler
    /// rather than registering a second — so saying so twice is not an error.
    /// </remarks>
    public void AddDefault()
    {
        HasDefaultScheduler = true;
    }

    /// <summary>
    /// Records configuration that belongs to every scheduler in the container, so that a scheduler
    /// registered after <c>ConfigureAllQuartzSchedulers</c> was called still receives it.
    /// </summary>
    public void AddConfigureAll(Action<IQuartzBuilder> configure)
    {
        configureAll.Add(configure);
    }

    /// <summary>
    /// Applies every recorded container-wide delegate to one scheduler.
    /// </summary>
    /// <remarks>
    /// Called from <c>AddQuartz</c> after the caller's own configuration callback, so a scheduler that
    /// says something for itself is not overruled by what the container says for everyone — options are
    /// last-wins and registration is first-wins, and this order gives the scheduler's own word the
    /// stronger side of both.
    /// </remarks>
    public void ApplyConfigureAll(IServiceCollection services, string? schedulerName)
    {
        // Indexed rather than foreach: a delegate is free to register a further scheduler, which would
        // append to this list while it is being walked.
        for (int i = 0; i < configureAll.Count; i++)
        {
            Apply(configureAll[i], services, schedulerName);
        }
    }

    /// <summary>
    /// Applies one container-wide delegate to one scheduler, unless it has already reached it.
    /// </summary>
    public void Apply(Action<IQuartzBuilder> configure, IServiceCollection services, string? schedulerName)
    {
        if (applied.Add((configure, schedulerName)))
        {
            configure(new QuartzBuilder(services, schedulerName));
        }
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
