using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Quartz;

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
        SchedulerBuilder schedulerBuilder = SchedulerBuilder.Create(options.ToNameValueCollection());
        DeferredServiceCollection deferredServices = new DeferredServiceCollection(services, serviceProvider, options, optionsName);
        ServiceCollectionQuartzConfigurator configurator = new ServiceCollectionQuartzConfigurator(deferredServices, schedulerBuilder, optionsName);

        configure(configurator, serviceProvider);

        // Copy all properties from the SchedulerBuilder back to QuartzOptions.
        // Properties that were not modified will have the same values they had before.
        foreach (string? key in schedulerBuilder.Properties.AllKeys)
        {
            if (key is not null)
            {
                options[key] = schedulerBuilder.Properties[key];
            }
        }
    }

    public void Configure(QuartzOptions options) => Configure(Options.DefaultName, options);
}
