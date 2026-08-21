using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Quartz.Configuration;

/// <summary>
/// Registers and binds the strongly typed Quartz options.
/// </summary>
/// <remarks>
/// <para>
/// Hierarchical configuration binds straight onto the options types — <c>Quartz:Scheduler:InstanceName</c>
/// becomes <see cref="QuartzSchedulerOptions.InstanceName"/> with no string-key translation in between.
/// The flat <c>quartz.*</c> keys are handled separately by the legacy adapter, which reshapes them into
/// this same hierarchy before binding.
/// </para>
/// <para>
/// Options that belong to a specific scheduler are registered as named options keyed by scheduler name,
/// so several schedulers can coexist in one container. The unnamed scheduler uses
/// <see cref="Options.DefaultName"/>.
/// </para>
/// </remarks>
internal static class QuartzTypedOptions
{
    internal const string SchedulerSection = "Scheduler";
    internal const string ThreadPoolSection = "ThreadPool";
    internal const string JobStoreSection = "JobStore";
    internal const string ClusteringSection = "Clustering";
    internal const string DataSourceSection = "DataSource";

    /// <summary>
    /// Registers the validators for every Quartz options type. Safe to call repeatedly.
    /// </summary>
    public static IServiceCollection AddQuartzOptionsValidation(this IServiceCollection services)
    {
        services.AddOptions();

        // The name registry is what the default scheduler's name is checked against, and it has to exist
        // whether or not a named scheduler was ever registered — otherwise validating the default
        // scheduler's options would fail to construct its own validator.
        SchedulerNameRegistry.For(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<QuartzSchedulerOptions>, QuartzSchedulerOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<QuartzSchedulerOptions>, DefaultSchedulerNameValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ThreadPoolOptions>, ThreadPoolOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InMemoryJobStoreOptions>, InMemoryJobStoreOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AdoJobStoreOptions>, AdoJobStoreOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ClusteringOptions>, ClusteringOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataSourceOptions>, DataSourceOptionsValidator>());

        return services;
    }

    /// <summary>
    /// Asks the host to validate one options type when it starts, rather than when whatever reads it is
    /// first resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for options a scheduler certainly resolves. Validating one it might not — <see
    /// cref="AdoJobStoreOptions"/> under an in-memory store, for instance — would turn settings nobody
    /// asked for into a startup failure, so those are declared where they are chosen instead.
    /// </para>
    /// <para>
    /// The host is what runs the validation: <c>Host.Build()</c> resolves <c>IStartupValidator</c> and
    /// calls it. In the container <see cref="QuartzSchedulerBuilder"/> builds there is nothing to do
    /// that, so this is inert there — <c>ValidateOnBuild</c> covers the object graph, not the values,
    /// and a bad value surfaces when the component reading it is built.
    /// </para>
    /// </remarks>
    internal static IServiceCollection ValidateOnStart<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this IServiceCollection services,
        string? schedulerName) where TOptions : class
    {
        services.AddOptions<TOptions>(schedulerName ?? Options.DefaultName).ValidateOnStart();
        return services;
    }

    /// <summary>
    /// Binds a hierarchical Quartz configuration section onto the strongly typed options.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="quartzSection">
    /// The Quartz configuration section, typically <c>configuration.GetSection("Quartz")</c>.
    /// </param>
    /// <param name="schedulerName">
    /// The scheduler these options belong to, or <see langword="null"/> for the default scheduler.
    /// </param>
    public static IServiceCollection BindQuartzOptions(
        this IServiceCollection services,
        IConfiguration quartzSection,
        string? schedulerName = null)
    {
        ArgumentNullException.ThrowIfNull(quartzSection);

        services.AddQuartzOptionsValidation();

        var name = schedulerName ?? Options.DefaultName;

        services.Configure<QuartzSchedulerOptions>(name, quartzSection.GetSection(SchedulerSection));
        services.Configure<ThreadPoolOptions>(name, quartzSection.GetSection(ThreadPoolSection));

        // In-memory and ADO.NET stores share the JobStore section but bind to different options types,
        // and which store a scheduler ends up with is not known while configuration is still being
        // registered. Both are bound; the one belonging to the store that is not used is never resolved.
        var jobStoreSection = quartzSection.GetSection(JobStoreSection);
        services.Configure<InMemoryJobStoreOptions>(name, jobStoreSection);
        services.Configure<AdoJobStoreOptions>(name, jobStoreSection);

        // Clustering is a sub-section rather than three more job store settings, because it is one
        // decision with two knobs attached to it rather than three independent ones.
        services.Configure<ClusteringOptions>(name, jobStoreSection.GetSection(ClusteringSection));

        // Data sources are named after themselves rather than after the scheduler, matching the way
        // the connection manager keys providers.
        foreach (var dataSource in quartzSection.GetSection(DataSourceSection).GetChildren())
        {
            services.Configure<DataSourceOptions>(dataSource.Key, dataSource);
        }

        // A named scheduler's instance name is always the name it was registered under.
        if (!string.IsNullOrEmpty(schedulerName))
        {
            services.Configure<QuartzSchedulerOptions>(schedulerName, options => options.InstanceName = schedulerName);
        }

        return services;
    }
}
