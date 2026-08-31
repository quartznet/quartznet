using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Plugins.Interrupt;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/quartz-plugins.md.
/// </summary>
public static class QuartzPluginsSamples
{
    public static void JobAndTriggerHistoryLogging(IServiceCollection services)
    {
        #region sample_plugins_history_logging

        services.AddQuartz(q =>
        {
            q.UseJobHistoryLogging(options =>
            {
                // each message left unset keeps the plugin's own default
                options.JobSuccessMessage = "Job {1}.{0} completed";
            });

            q.UseTriggerHistoryLogging();
        });

        #endregion
    }

    public static void StructuredJobLogging(IServiceCollection services)
    {
        #region sample_plugins_structured_job_logging

        services.AddQuartz(q =>
        {
            q.UseStructuredJobLogging(options =>
            {
                // Optional; each template left unset keeps the plugin's own default.
                options.JobFailedMessage = "Job {JobGroup}.{JobName} failed: {ExceptionMessage}";
            });
        });

        #endregion
    }

    public static void StructuredTriggerLogging(IServiceCollection services)
    {
        #region sample_plugins_structured_trigger_logging

        services.AddQuartz(q =>
        {
            q.UseStructuredTriggerLogging(options =>
            {
                // Optional; each template left unset keeps the plugin's own default.
                options.TriggerMisfiredMessage = "Trigger {TriggerGroup}.{TriggerName} misfired at {FireTime}";
            });
        });

        #endregion
    }

    public static void ShutdownHook(IServiceCollection services)
    {
        #region sample_plugins_shutdown_hook

        services.AddQuartz(q => q.UseShutdownHook(options => options.CleanShutdown = true));

        #endregion
    }

    public static void PluginOptionsFromConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        #region sample_plugins_options_from_configuration

        // A plugin's options are the scheduler's own named options, so a configuration section binds
        // onto them like any other. The callback below is applied over whatever the section said.
        services.Configure<FileSchedulingOptions>(configuration.GetSection("Quartz:Xml"));

        services.AddQuartz(q => q.UseXmlSchedulingConfiguration(x => x.ScanInterval = TimeSpan.FromMinutes(1)));

        #endregion
    }

    public static void XmlSchedulingConfiguration(IServiceCollection services)
    {
        #region sample_plugins_xml_scheduling

        services.AddQuartz(q =>
        {
            q.UseXmlSchedulingConfiguration(x =>
            {
                x.Files.Add("~/quartz_jobs.config");
                x.ScanInterval = TimeSpan.FromMinutes(1);
                x.FailOnFileNotFound = true;
                x.FailOnSchedulingError = true;
            });
        });

        #endregion
    }

    public static void JsonSchedulingConfiguration(IServiceCollection services)
    {
        #region sample_plugins_json_scheduling

        services.AddQuartz(q =>
        {
            q.UseJsonSchedulingConfiguration(x =>
            {
                x.Files.Add("quartz_jobs.json");
                x.ScanInterval = TimeSpan.FromMinutes(1);
                x.FailOnFileNotFound = true;
                x.FailOnSchedulingError = true;
            });
        });

        #endregion
    }

    public static void JobAutoInterrupt(IServiceCollection services)
    {
        #region sample_plugins_job_auto_interrupt

        services.AddQuartz(q => q.UseJobAutoInterrupt(options =>
        {
            // the default, applied to every job that opts in
            options.DefaultMaxRunTime = TimeSpan.FromMinutes(5);
        }));

        #endregion
    }

    public static void JobAutoInterruptJobData()
    {
        #region sample_plugins_job_auto_interrupt_job_data

        IJobDetail job = JobBuilder.Create<SlowJob>()
            .WithIdentity("slowJob")
            .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
            // allow only five seconds for this job, overriding default configuration.
            // the value is milliseconds, and either a number or a string holding one works
            .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "5000")
            .Build();

        #endregion
    }

    public static void AddPluginShapes(IServiceCollection services)
    {
        #region sample_plugins_add_plugin

        services.AddQuartz(q =>
        {
            // the container constructs it, so it gets constructor injection
            q.AddPlugin<MyPlugin>();

            // you construct it
            q.AddPlugin(provider => new MyPlugin(provider.GetRequiredService<IMyPluginDependency>()));

            // it takes an IOptions<MyPluginOptions> of its own
            q.AddPlugin<MyPlugin, MyPluginOptions>(options => options.SomeSetting = "value");
        });

        #endregion
    }

    public static void AddPluginNames(IQuartzBuilder q)
    {
        #region sample_plugins_add_plugin_names

        q.AddPlugin<MyPlugin>("myPlugin");
        q.AddPlugin(provider => new MyPlugin(), "myPlugin");
        q.AddPlugin<MyPlugin, MyPluginOptions>(options => options.SomeSetting = "value", "myPlugin");

        #endregion
    }

    public static async ValueTask UsingTheExtension(IServiceCollection services)
    {
        #region sample_plugins_using_the_extension

        // under a host
        services.AddQuartz(q => q.UseMyPlugin(options => options.SomeSetting = "value"));

        // standalone, without an application container
        var builder = QuartzSchedulerBuilder.Create();
        builder.UseMyPlugin(options => options.SomeSetting = "value");

        var scheduler = await builder.BuildScheduler();

        #endregion

        await scheduler.Shutdown();
    }
}

#region sample_plugins_authoring_extension

public static class MyPluginConfigurationExtensions
{
    public static IQuartzBuilder UseMyPlugin(
        this IQuartzBuilder builder,
        Action<MyPluginOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new MyPluginOptions();
        configure?.Invoke(options);

        // companion services your plugin needs injected
        builder.Services.TryAddSingleton<IMyPluginDependency, MyPluginDependency>();

        return builder.AddPlugin<MyPlugin>(
            provider =>
            {
                var plugin = ActivatorUtilities.CreateInstance<MyPlugin>(provider);
                plugin.SomeSetting = options.SomeSetting;
                return plugin;
            },
            name: "myPlugin");
    }
}

public sealed class MyPluginOptions
{
    public string? SomeSetting { get; set; }
}

#endregion
