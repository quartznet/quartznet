---

title: Plugins
---

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides some useful ready-made plugins for your convenience.

Quartz provides an interface (`ISchedulerPlugin`, in the `Quartz.Extensibility` namespace) for plugging-in additional functionality.

The plugins that ship in this package live in the `Quartz.Plugins.*` namespaces — `Quartz.Plugins.History`,
`Quartz.Plugins.Interrupt`, `Quartz.Plugins.Json`, `Quartz.Plugins.Management` and `Quartz.Plugins.Xml`, matching the
assembly and NuGet package name. In 3.x they were the singular `Quartz.Plugin.*`; a `quartz.plugin.<name>.type`
naming the old spelling still resolves, with a warning.
They provide functionality such as auto-scheduling of jobs upon scheduler startup, logging a history of job and trigger events,
and ensuring that the scheduler shuts down cleanly when the process exits.

## Installation

You need to add NuGet package reference to your project which uses Quartz.

```shell
dotnet add package Quartz.Plugins
```

## Configuration

Every plugin in this package has an extension method that adds and configures it in one call. That is the
way to reach for; the flat keys, in the format `quartz.plugin.{name-to-refer-with}.{property}`, are the 3.x
spelling of the same thing and still work.

| Plugin | Extension | Options |
|---|---|---|
| `LoggingJobHistoryPlugin` | `UseJobHistoryLogging(…)` | `JobHistoryLoggingOptions` |
| `LoggingTriggerHistoryPlugin` | `UseTriggerHistoryLogging(…)` | `TriggerHistoryLoggingOptions` |
| `StructuredLoggingJobHistoryPlugin` | `UseStructuredJobLogging(…)` | `JobHistoryLoggingOptions` |
| `StructuredLoggingTriggerHistoryPlugin` | `UseStructuredTriggerLogging(…)` | `TriggerHistoryLoggingOptions` |
| `ShutdownHookPlugin` | `UseShutdownHook(…)` | `ShutdownHookOptions` |
| `XmlSchedulingDataProcessorPlugin` | `UseXmlSchedulingConfiguration(…)` | `FileSchedulingOptions` |
| `JsonSchedulingDataProcessorPlugin` | `UseJsonSchedulingConfiguration(…)` | `FileSchedulingOptions` |
| `JobInterruptMonitorPlugin` | `UseJobAutoInterrupt(…)` | `JobAutoInterruptOptions` |

They hang off `IQuartzBuilder`, so they work the same under `AddQuartz` and on a standalone
`QuartzSchedulerBuilder`. See the
[configuration reference](../configuration/reference.md#listeners-calendars-and-plugins) for how a plugin
is registered and named.

An options type in that table is the scheduler's own named options, so a plugin is configurable from
`appsettings.json` like anything else the container builds — bind the section and the values reach the
plugin:

<!-- snippet: sample_plugins_options_from_configuration -->
```csharp
// A plugin's options are the scheduler's own named options, so a configuration section binds
// onto them like any other. The callback below is applied over whatever the section said.
services.Configure<FileSchedulingOptions>(configuration.GetSection("Quartz:Xml"));

services.AddQuartz(q => q.UseXmlSchedulingConfiguration(x => x.ScanInterval = TimeSpan.FromMinutes(1)));
```
<!-- endSnippet -->

The three sources are applied in the order that makes code the last word: the flat
`quartz.plugin.{name}.{property}` keys first, then the values bound onto the options, then the callback
passed to the extension method. A setting the callback says nothing about keeps whatever the keys or the
configuration section gave it, so configuring a plugin in code does not discard the rest of its
configuration — it only overrides the parts it names. Under `AddQuartz("name", …)` the options are that
scheduler's, so bind them with `services.Configure<TOptions>("name", section)`.

## Features

### LoggingJobHistoryPlugin

Logs a history of all job executions (and execution vetoes) and writes the entries to configured logging
infrastructure. `LoggingTriggerHistoryPlugin` does the same for trigger firings, misfires and completions.

<!-- snippet: sample_plugins_history_logging -->
```csharp
services.AddQuartz(q =>
{
    q.UseJobHistoryLogging(options =>
    {
        // each message left unset keeps the plugin's own default
        options.JobSuccessMessage = "Job {1}.{0} completed";
    });

    q.UseTriggerHistoryLogging();
});
```
<!-- endSnippet -->

Both use index-based placeholders in their messages. Prefer the structured plugins below unless you have
existing message templates to keep.

### StructuredLoggingJobHistoryPlugin

Structured logging alternative to `LoggingJobHistoryPlugin`. Uses named message template parameters (e.g. `{JobName}`, `{TriggerGroup}`) instead of index-based placeholders, making log output compatible with structured logging sinks like Serilog and NLog. This avoids template cache memory leaks that can occur with the original plugin.

Message templates can be customized via properties. When customizing, the parameter names in templates are positionally mapped, so they must appear in the same order as the defaults.

Available template properties:

| Property | Parameters (in order) |
|---|---|
| `JobToBeFiredMessage` | `{JobGroup}`, `{JobName}`, `{TriggerGroup}`, `{TriggerName}`, `{FireTime}`, `{ScheduledFireTime}`, `{NextFireTime}`, `{RefireCount}` |
| `JobSuccessMessage` | `{JobGroup}`, `{JobName}`, `{FireTime}`, `{TriggerGroup}`, `{TriggerName}`, `{Result}` |
| `JobFailedMessage` | `{JobGroup}`, `{JobName}`, `{FireTime}`, `{TriggerGroup}`, `{TriggerName}`, `{ExceptionMessage}` |
| `JobWasVetoedMessage` | `{JobGroup}`, `{JobName}`, `{TriggerGroup}`, `{TriggerName}`, `{FireTime}` |

**DI configuration:**

<!-- snippet: sample_plugins_structured_job_logging -->
```csharp
services.AddQuartz(q =>
{
    q.UseStructuredJobLogging(options =>
    {
        // Optional; each template left unset keeps the plugin's own default.
        options.JobFailedMessage = "Job {JobGroup}.{JobName} failed: {ExceptionMessage}";
    });
});
```
<!-- endSnippet -->

::: tip
Recommended over `LoggingJobHistoryPlugin` when using structured logging providers (Serilog, NLog, etc.).
:::

### StructuredLoggingTriggerHistoryPlugin

Structured logging alternative to `LoggingTriggerHistoryPlugin`. Logs trigger firings, misfires, and completions using named message template parameters for structured logging compatibility.

Message templates can be customized via properties. When customizing, the parameter names in templates are positionally mapped, so they must appear in the same order as the defaults.

Available template properties:

| Property | Parameters (in order) |
|---|---|
| `TriggerFiredMessage` | `{TriggerGroup}`, `{TriggerName}`, `{JobGroup}`, `{JobName}`, `{FireTime}`, `{ScheduledFireTime}`, `{NextFireTime}`, `{RefireCount}` |
| `TriggerMisfiredMessage` | `{TriggerGroup}`, `{TriggerName}`, `{JobGroup}`, `{JobName}`, `{FireTime}`, `{ScheduledFireTime}`, `{NextFireTime}` |
| `TriggerCompleteMessage` | `{TriggerGroup}`, `{TriggerName}`, `{JobGroup}`, `{JobName}`, `{CompletedTime}`, `{ScheduledFireTime}`, `{NextFireTime}`, `{TriggerInstructionCode}` |

**DI configuration:**

<!-- snippet: sample_plugins_structured_trigger_logging -->
```csharp
services.AddQuartz(q =>
{
    q.UseStructuredTriggerLogging(options =>
    {
        // Optional; each template left unset keeps the plugin's own default.
        options.TriggerMisfiredMessage = "Trigger {TriggerGroup}.{TriggerName} misfired at {FireTime}";
    });
});
```
<!-- endSnippet -->

::: tip
Recommended over `LoggingTriggerHistoryPlugin` when using structured logging providers (Serilog, NLog, etc.).
:::

### ShutdownHookPlugin

This plugin catches the event of the process terminating (such as upon a Ctrl-C) and tells the scheduler to
shut down.

<!-- snippet: sample_plugins_shutdown_hook -->
```csharp
services.AddQuartz(q => q.UseShutdownHook(options => options.CleanShutdown = true));
```
<!-- endSnippet -->

`CleanShutdown` decides whether the shutdown waits for jobs that are still running. Under a host,
[the hosted service](hosted-services-integration.md) already stops the scheduler with the application, so
this plugin is for a scheduler that has no host to stop it.

### XmlSchedulingDataProcessorPlugin

This plugin loads XML file(s) to add jobs and schedule them with triggers as the scheduler is initialized, and can optionally periodically scan the file for changes.

<!-- snippet: sample_plugins_xml_scheduling -->
```csharp
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
```
<!-- endSnippet -->

::: warning
The periodically scanning of files for changes is not currently supported in a clustered environment.
:::

A file that declares the same job or trigger key — name **and** group — twice is rejected, whatever
`<processing-directives>` it carries. `<overwrite-existing-data>` and `<ignore-duplicates>` say how
the file relates to the scheduler, and neither can say anything about how the file relates to itself.
The [ProcessingDirectives](../configuration/json.md#processingdirectives) section has the details; they
apply to both formats.

### JsonSchedulingDataProcessorPlugin

This plugin loads JSON file(s) to add jobs and schedule them with triggers as the scheduler is initialized, and can optionally periodically scan the file for changes. It is the JSON analog of `XmlSchedulingDataProcessorPlugin`.

::: warning
The periodically scanning of files for changes is not currently supported in a clustered environment.
:::

**DI configuration:**

<!-- snippet: sample_plugins_json_scheduling -->
```csharp
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
```
<!-- endSnippet -->

See [JSON Configuration](../configuration/json.md) for the full JSON file format and trigger type reference.

### JobInterruptMonitorPlugin

This plugin catches the event of job running for a long time (more than the configured max time) and tells the scheduler to "try" interrupting it if enabled.

<!-- snippet: sample_plugins_job_auto_interrupt -->
```csharp
services.AddQuartz(q => q.UseJobAutoInterrupt(options =>
{
    // the default, applied to every job that opts in
    options.DefaultMaxRunTime = TimeSpan.FromMinutes(5);
}));
```
<!-- endSnippet -->

Each job configuration needs to have `JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable` key's value set to true in order for plugin to monitor the execution timeout.
Jobs can also define custom timeout value instead of global default by using key `JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime`.

<!-- snippet: sample_plugins_job_auto_interrupt_job_data -->
```csharp
IJobDetail job = JobBuilder.Create<SlowJob>()
    .WithIdentity("slowJob")
    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
    // allow only five seconds for this job, overriding default configuration.
    // the value is milliseconds, and either a number or a string holding one works
    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "5000")
    .Build();
```
<!-- endSnippet -->

Both `AutoInterruptable` and `MaxRunTime` are read from the merged job data map, so a trigger's data map can also enable interruption or override the timeout for its own fires.

Only the execution that exceeded its allowed run time is interrupted — the plugin monitors each fire instance separately, so concurrent executions of the same job are unaffected. Executions vetoed by a trigger listener do not arm the interrupt timer.

## Adding a plugin

`AddPlugin` comes in the same three shapes as the listener registrations: the container builds the
plugin, you build it, or you configure options it is given.

<!-- snippet: sample_plugins_add_plugin -->
```csharp
services.AddQuartz(q =>
{
    // the container constructs it, so it gets constructor injection
    q.AddPlugin<MyPlugin>();

    // you construct it
    q.AddPlugin(provider => new MyPlugin(provider.GetRequiredService<IMyPluginDependency>()));

    // it takes an IOptions<MyPluginOptions> of its own
    q.AddPlugin<MyPlugin, MyPluginOptions>(options => options.SomeSetting = "value");
});
```
<!-- endSnippet -->

Every shape takes an optional name as its last argument:

<!-- snippet: sample_plugins_add_plugin_names -->
```csharp
q.AddPlugin<MyPlugin>("myPlugin");
q.AddPlugin(provider => new MyPlugin(), "myPlugin");
q.AddPlugin<MyPlugin, MyPluginOptions>(options => options.SomeSetting = "value", "myPlugin");
```
<!-- endSnippet -->

The name is how the scheduler refers to the plugin, and some plugins derive persisted job and trigger
keys from it — so it is part of the deployment's identity rather than a label. It is also the name a
`quartz.plugin.{name}.*` key configures the same plugin under, which is what lets a plugin added in
code be configured from a file. Left unset, the plugin's type name is used. The plugins shipped with
Quartz use their conventional short names (`xml`, `json`, `jobHistory`, …) for that reason.

The options of the third shape belong to the scheduler they were added to, like every other
per-scheduler setting: two schedulers can add the same plugin with the same options type and each
plugin sees its own configuration. They are named options under the scheduler's name, so a plugin on
`services.AddQuartz("reporting", …)` is configured by `services.Configure<MyPluginOptions>("reporting", …)`
as well — a plain `services.Configure<MyPluginOptions>(…)` configures the default scheduler's.

Take them as `IOptions<MyPluginOptions>` for a fixed value, or as `IOptionsMonitor<MyPluginOptions>`
to follow a reloading configuration source. `CurrentValue` is your scheduler's instance, `Get(name)`
is whichever instance you name, and `OnChange` fires for your scheduler's options only — so a plugin
watching for changes is never handed a sibling scheduler's configuration as though it were its own.

## Authoring plugin configuration extensions

`ISchedulerPlugin` declares three members, but only `Initialize` has to be written: `Start` and
`Shutdown` have default implementations that do nothing, for the common plugin that does all its work
at initialization — attaching a listener, registering a resolver. Implement them when there is
something that cannot happen until the scheduler is running, or resources to release when it stops.

When you write your own `ISchedulerPlugin`, offer the same experience as the built-in plugins with an
extension method on `IQuartzBuilder`. Take an options object of your own, apply it to the plugin, and
register the plugin under its conventional name:

<!-- snippet: sample_plugins_authoring_extension -->
```csharp
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
```
<!-- endSnippet -->

The same extension method works wherever an `IQuartzBuilder` does, which is both configuration styles:

<!-- snippet: sample_plugins_using_the_extension -->
```csharp
// under a host
services.AddQuartz(q => q.UseMyPlugin(options => options.SomeSetting = "value"));

// standalone, without an application container
var builder = QuartzSchedulerBuilder.Create();
builder.UseMyPlugin(options => options.SomeSetting = "value");

var scheduler = await builder.BuildScheduler();
```
<!-- endSnippet -->

Configuration written this way and configuration written as `quartz.plugin.myPlugin.someSetting`
reach the same plugin instance, because they agree on its name: the properties are applied to the
plugin the code registered rather than building a second copy of it.
