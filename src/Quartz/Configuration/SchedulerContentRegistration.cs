using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Configuration;

/// <summary>
/// Registers the content a scheduler carries — its listeners and calendars — under that scheduler's
/// service key.
/// </summary>
/// <remarks>
/// The convention is the one every other part of a scheduler follows (see
/// <see cref="QuartzServiceRegistration"/>): the scheduler's name is the service key, and the default
/// scheduler has none, so its content is registered unkeyed. Content registered for one scheduler is
/// therefore invisible to every other scheduler in the container, without anything having to be filtered
/// by name after the fact.
/// </remarks>
internal static class SchedulerContentRegistration
{
    /// <summary>
    /// Registers a piece of content the caller has already built.
    /// </summary>
    public static void Add<TContent>(IQuartzBuilder builder, TContent content) where TContent : class
    {
        var key = Key(builder);
        if (key is null)
        {
            builder.Services.AddSingleton(content);
        }
        else
        {
            builder.Services.AddKeyedSingleton(key, content);
        }
    }

    /// <summary>
    /// Registers a piece of content built from the container.
    /// </summary>
    /// <remarks>
    /// Built through the scheduler-scoped provider, so content that depends on the scheduler it belongs to
    /// is given that scheduler's parts rather than the default scheduler's.
    /// </remarks>
    public static void Add<TContent>(IQuartzBuilder builder, Func<IServiceProvider, TContent> factory) where TContent : class
    {
        var key = Key(builder);
        if (key is null)
        {
            builder.Services.AddSingleton(factory);
        }
        else
        {
            builder.Services.AddKeyedSingleton<TContent>(
                key,
                (provider, serviceKey) => factory(SchedulerScopedServiceProvider.For(provider, serviceKey)));
        }
    }

    /// <summary>
    /// The service key for this builder's scheduler, or <see langword="null"/> for the default one.
    /// </summary>
    private static string? Key(IQuartzBuilder builder)
    {
        return builder.SchedulerName.Length == 0 ? null : builder.SchedulerName;
    }
}
