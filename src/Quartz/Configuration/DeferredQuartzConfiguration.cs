using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// Deferred Quartz configuration that runs when <see cref="IOptions{QuartzOptions}"/> is first
/// resolved, providing access to <see cref="IServiceProvider"/> during configuration.
/// </summary>
internal sealed class DeferredQuartzConfiguration : IConfigureNamedOptions<QuartzOptions>
{
    private readonly IServiceProvider serviceProvider;
    private readonly IServiceCollection services;
    private readonly Action<IServiceCollectionQuartzConfigurator, IServiceProvider> configure;
    private readonly string optionsName;

    public DeferredQuartzConfiguration(
        IServiceProvider serviceProvider,
        IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator, IServiceProvider> configure,
        string optionsName)
    {
        this.serviceProvider = serviceProvider;
        this.services = services;
        this.configure = configure;
        this.optionsName = optionsName;
    }

    public void Configure(string? name, QuartzOptions options)
    {
        // Only apply to the matching options name
        if (optionsName.Length == 0)
        {
            if (name != Options.DefaultName)
            {
                return;
            }
        }
        else
        {
            if (name != optionsName)
            {
                return;
            }
        }

        // Create a SchedulerBuilder from the current options state so the deferred
        // configuration can read and override any properties set by earlier configurators.
        var schedulerBuilder = SchedulerBuilder.Create(options.ToNameValueCollection());
        var deferredServices = new DeferredServiceCollection(services, serviceProvider, options, optionsName);
        var configurator = new ServiceCollectionQuartzConfigurator(deferredServices, schedulerBuilder, optionsName);

        configure(configurator, serviceProvider);

        // Re-force the scheduler instance name for named schedulers so it cannot drift
        // via SetProperty() or Properties[] in the deferred lambda. This mirrors the
        // same guard in the immediate AddQuartz(name, properties, configure) overload.
        if (optionsName.Length > 0)
        {
            schedulerBuilder.Properties[StdSchedulerFactory.PropertySchedulerInstanceName] = optionsName;
        }

        // Copy all properties from the SchedulerBuilder back to QuartzOptions.
        // Properties that were not modified will have the same values they had before.
        foreach (var key in schedulerBuilder.Properties.AllKeys)
        {
            if (key is not null)
            {
                options[key] = schedulerBuilder.Properties[key];
            }
        }
    }

    public void Configure(QuartzOptions options) => Configure(Options.DefaultName, options);
}
