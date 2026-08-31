using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Quartz;

/// <summary>
/// Registers Quartz on a host application builder, which is how an application built by
/// <c>Host.CreateApplicationBuilder</c> or <c>WebApplication.CreateBuilder</c> adds anything else.
/// </summary>
/// <remarks>
/// <para>
/// These are the <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/> overloads with
/// the application's configuration already found. A builder has both halves — its services and its
/// configuration — so <c>builder.AddQuartz(…)</c> reads the <c>Quartz</c> configuration section without
/// being handed it, which is what the two-line <c>builder.Services.AddQuartz(builder.Configuration.GetSection("Quartz"), …)</c>
/// was for.
/// </para>
/// <para>
/// Nothing is removed: <c>builder.Services.AddQuartz(…)</c> keeps working and means what it always did,
/// including with a configuration section of a different name.
/// </para>
/// </remarks>
public static class QuartzHostApplicationBuilderExtensions
{
    /// <summary>
    /// The configuration section Quartz is described by. It is the name every sample, every documentation
    /// page and <c>QuartzOptions</c>' own binding use, so a builder that goes looking for one looks here.
    /// </summary>
    private const string ConfigurationSectionName = "Quartz";

    /// <summary>
    /// Registers a Quartz scheduler, configured from the application's <c>Quartz</c> configuration section.
    /// </summary>
    /// <remarks>
    /// An application with no such section gets a scheduler configured entirely by
    /// <paramref name="configure"/>, which is what <c>services.AddQuartz(configure)</c> would have given
    /// it.
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Configures the scheduler, over whatever the section said.</param>
    public static IHostApplicationBuilder AddQuartz(
        this IHostApplicationBuilder builder,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddQuartz(Section(builder), configure);
        return builder;
    }

    /// <summary>
    /// Registers a named Quartz scheduler, configured from the application's <c>Quartz</c> configuration
    /// section.
    /// </summary>
    /// <remarks>
    /// The name is the scheduler's, exactly as in <c>services.AddQuartz(name, …)</c> — a string means a
    /// scheduler here as it does everywhere else in Quartz, never a configuration section. Its settings
    /// are read from <c>Quartz:Schedulers:&lt;name&gt;</c> when the section describes several schedulers.
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    /// <param name="name">The scheduler's name.</param>
    /// <param name="configure">Configures the scheduler, over whatever the section said.</param>
    public static IHostApplicationBuilder AddQuartz(
        this IHostApplicationBuilder builder,
        string name,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        builder.Services.AddQuartz(name, Section(builder), configure);
        return builder;
    }

    /// <summary>
    /// Registers one named Quartz scheduler per child of the <c>Quartz</c> section's <c>Schedulers</c>
    /// sub-section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <see cref="QuartzServiceCollectionExtensions.AddQuartzSchedulers(Microsoft.Extensions.DependencyInjection.IServiceCollection, IConfiguration, Action{IQuartzBuilder})"/>
    /// with the section found for you, and it says exactly what that one says — including refusing a
    /// section that describes one scheduler rather than several.
    /// </para>
    /// <para>
    /// The receiver decides where configuration comes from, and it decides it the same way for every
    /// method here: a service collection is <em>handed</em> its configuration, a builder already holds
    /// its own. That is why this takes no <see cref="IConfiguration"/> and the service collection
    /// overload requires one — the same difference <c>AddQuartz</c> has, for the same reason. Neither
    /// receiver goes looking for a configuration it was not given: <c>services.AddQuartz(configure)</c>
    /// means a scheduler configured entirely in code, not one that quietly reads whatever
    /// <see cref="IConfiguration"/> the container happens to hold, and there is no
    /// <c>services.AddQuartzSchedulers(configure)</c> because a fan-out with nothing to fan out over is
    /// not a scheduler registration at all.
    /// </para>
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Applied to every scheduler described by the section.</param>
    public static IHostApplicationBuilder AddQuartzSchedulers(
        this IHostApplicationBuilder builder,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddQuartzSchedulers(Section(builder), configure);
        return builder;
    }

    /// <summary>
    /// Adds the hosted service that starts and stops every scheduler in the container with the
    /// application.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Configures how the schedulers are started and stopped.</param>
    public static IHostApplicationBuilder AddQuartzHostedService(
        this IHostApplicationBuilder builder,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddQuartzHostedService(configure);
        return builder;
    }

    /// <summary>
    /// Adds a hosted service of your own deriving from <see cref="QuartzHostedService"/>.
    /// </summary>
    /// <typeparam name="T">Type extending the <see cref="QuartzHostedService"/> class.</typeparam>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Configures how the schedulers are started and stopped.</param>
    public static IHostApplicationBuilder AddQuartzHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IHostApplicationBuilder builder,
        Action<QuartzHostedServiceOptions>? configure = null)
        where T : QuartzHostedService
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddQuartzHostedService<T>(configure);
        return builder;
    }

    /// <summary>
    /// Configures how one named scheduler is started and stopped by the hosted service.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="schedulerName">The name the scheduler was registered under.</param>
    /// <param name="configure">Configures how that scheduler is started and stopped.</param>
    public static IHostApplicationBuilder AddQuartzHostedService(
        this IHostApplicationBuilder builder,
        string schedulerName,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddQuartzHostedService(schedulerName, configure);
        return builder;
    }

    private static IConfiguration Section(IHostApplicationBuilder builder)
    {
        return builder.Configuration.GetSection(ConfigurationSectionName);
    }
}
