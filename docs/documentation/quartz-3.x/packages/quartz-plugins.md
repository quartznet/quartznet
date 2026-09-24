---

title : Plugins
---

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides ready-made plugins.

## Installation

```shell
Install-Package Quartz.Plugins
```

## Configuration

Configure plugins with the DI configuration extensions or with configuration keys of the form `quartz.plugin.{name-to-refer-with}.{property}`. The [configuration reference](../configuration/reference.html#plug-ins) shows how to configure each plugin.

## Features

### LoggingJobHistoryPlugin

Logs every job execution (and execution veto) to the configured logging infrastructure.

### StructuredLoggingJobHistoryPlugin

Structured logging alternative to `LoggingJobHistoryPlugin`. It uses named message template parameters (e.g. `{JobName}`, `{TriggerGroup}`) instead of index-based placeholders, for structured logging sinks such as Serilog and NLog. It also avoids the template cache memory leaks the original plugin can cause.

Message templates can be customized through properties. Parameters are mapped by position, so a custom template must keep them in the default order:

| Property | Parameters (in order) |
|---|---|
| `JobToBeFiredMessage` | `{JobGroup}`, `{JobName}`, `{TriggerGroup}`, `{TriggerName}`, `{FireTime}`, `{ScheduledFireTime}`, `{NextFireTime}`, `{RefireCount}` |
| `JobSuccessMessage` | `{JobGroup}`, `{JobName}`, `{FireTime}`, `{TriggerGroup}`, `{TriggerName}`, `{Result}` |
| `JobFailedMessage` | `{JobGroup}`, `{JobName}`, `{FireTime}`, `{TriggerGroup}`, `{TriggerName}`, `{ExceptionMessage}` |
| `JobWasVetoedMessage` | `{JobGroup}`, `{JobName}`, `{TriggerGroup}`, `{TriggerName}`, `{FireTime}` |

**DI configuration:**

```csharp
services.AddQuartz(q =>
{
    q.UseStructuredJobLogging();
});
```

::: tip
Recommended over `LoggingJobHistoryPlugin` when using structured logging providers (Serilog, NLog, etc.).
:::

### StructuredLoggingTriggerHistoryPlugin

Structured logging alternative to `LoggingTriggerHistoryPlugin`. Logs trigger firings, misfires and completions with named message template parameters.

Message templates can be customized through properties. Parameters are mapped by position, so a custom template must keep them in the default order:

| Property | Parameters (in order) |
|---|---|
| `TriggerFiredMessage` | `{TriggerGroup}`, `{TriggerName}`, `{JobGroup}`, `{JobName}`, `{FireTime}`, `{ScheduledFireTime}`, `{NextFireTime}`, `{RefireCount}` |
| `TriggerMisfiredMessage` | `{TriggerGroup}`, `{TriggerName}`, `{JobGroup}`, `{JobName}`, `{FireTime}`, `{ScheduledFireTime}`, `{NextFireTime}` |
| `TriggerCompleteMessage` | `{TriggerGroup}`, `{TriggerName}`, `{JobGroup}`, `{JobName}`, `{CompletedTime}`, `{ScheduledFireTime}`, `{NextFireTime}`, `{TriggerInstructionCode}` |

**DI configuration:**

```csharp
services.AddQuartz(q =>
{
    q.UseStructuredTriggerLogging();
});
```

::: tip
Recommended over `LoggingTriggerHistoryPlugin` when using structured logging providers (Serilog, NLog, etc.).
:::

### ShutdownHookPlugin

Shuts the scheduler down when the VM terminates (such as on CTRL-C).

### XMLSchedulingDataProcessorPlugin

Loads XML file(s) and schedules their jobs and triggers when the scheduler initializes. It can also scan the files for changes periodically.

::: warning
Periodic scanning for changes is not supported in a clustered environment.
:::

### JobInterruptMonitorPlugin

If enabled, asks the scheduler to try interrupting a job that runs longer than the configured maximum time.

::: tip
Quartz 3.3 or later required.
:::

The plugin monitors only jobs whose `JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable` value is true. A job can override the global default timeout with `JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime`.

```csharp
var job = JobBuilder.Create<SlowJob>()
    .WithIdentity("slowJob")
    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
    // allow only five seconds for this job, overriding default configuration
    .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, TimeSpan.FromSeconds(5).TotalMilliseconds.ToString(CultureInfo.InvariantCulture))
    .Build();
```

* `AutoInterruptable` and `MaxRunTime` are read from the merged job data map, so a trigger's data map can enable interruption or override the timeout for its own fires.
* Only the execution that exceeded its run time is interrupted. Each fire instance is monitored separately, so concurrent executions of the same job are unaffected.
* Executions vetoed by a trigger listener do not start the interrupt timer.

## Authoring plugin configuration extensions

::: tip
Quartz 3.19 or later required.
:::

To give your own `ISchedulerPlugin` strongly typed configuration like the built-in plugins, write an extension method on `IPropertyConfigurationRoot`. The `UsePlugin` helper does the registration: it sets the `quartz.plugin.{name}.type` property and, when Microsoft DI (`AddQuartz`) backs the configuration, registers the plugin type in the container so it is constructed with constructor injection.

```csharp
public static class MyPluginConfigurationExtensions
{
    public static T UseMyPlugin<T>(this T configurer, Action<MyPluginOptions>? configure = null)
        where T : IPropertyConfigurationRoot
    {
        configurer.UsePlugin<MyPlugin>("myPlugin");

        // optional: register companion services your plugin needs injected;
        // returns false when there is no container (plain SchedulerBuilder usage)
        configurer.TryRegisterSingleton<IMyPluginDependency, MyPluginDependency>();

        configure?.Invoke(new MyPluginOptions(configurer));
        return configurer;
    }
}
```

Strongly typed options derive from `PropertiesSetter` with the plugin's property prefix. Each property setter maps to a `quartz.plugin.{name}.{property}` key, which is applied to the plugin's public setter:

```csharp
public sealed class MyPluginOptions : PropertiesSetter
{
    internal MyPluginOptions(IPropertySetter parent) : base(parent, "quartz.plugin.myPlugin")
    {
    }

    // maps to quartz.plugin.myPlugin.someSetting and MyPlugin.SomeSetting setter
    public string SomeSetting
    {
        set => SetProperty("someSetting", value);
    }
}
```

The extension method works with every configuration style:

```csharp
// Microsoft DI - plugin is constructed by the container, constructor injection available
services.AddQuartz(q =>
{
    q.UseMyPlugin(options =>
    {
        options.SomeSetting = "value";
    });
});

// plain SchedulerBuilder - plugin is created via reflection and needs
// a public parameterless constructor
var scheduler = await SchedulerBuilder.Create()
    .UseMyPlugin()
    .BuildScheduler();
```
