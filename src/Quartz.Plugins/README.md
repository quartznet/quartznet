# Quartz.Plugins

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides a set of ready-made plugins for common scheduling needs.

## Installation

```shell
dotnet add package Quartz.Plugins
```

## Included plugins

* **LoggingJobHistoryPlugin** / **LoggingTriggerHistoryPlugin** — log a history of job executions and trigger firings.
* **StructuredLoggingJobHistoryPlugin** / **StructuredLoggingTriggerHistoryPlugin** — structured-logging equivalents using named message-template parameters (Serilog, NLog, …); recommended when using structured logging sinks.
* **XMLSchedulingDataProcessorPlugin** / **JsonSchedulingDataProcessorPlugin** — load jobs and triggers from XML or JSON files, optionally re-scanning for changes.
* **ShutdownHookPlugin** — shuts the scheduler down when the process terminates.
* **JobInterruptMonitorPlugin** — interrupts jobs that run longer than a configured maximum (Quartz 3.3+).

## Usage

Plugins are enabled via DI extensions or configuration keys (`quartz.plugin.{name}.{property}`):

```csharp
services.AddQuartz(q =>
{
    q.UseStructuredJobLogging();
    q.UseStructuredTriggerLogging();
});
```

## Authoring configuration extensions

When you write your own `ISchedulerPlugin`, you can offer the same strongly typed `q.UseMyPlugin()` experience as the built-in plugins. Write an extension method on `IPropertyConfigurationRoot` and call the `UsePlugin<TPlugin>(name)` helper (shipped in the core `Quartz` package): it sets the `quartz.plugin.{name}.type` property and, when configuration is backed by Microsoft DI (`AddQuartz`), registers the plugin into the container so it is constructed with constructor injection. Use `TryRegisterSingleton<TService, TImplementation>()` to register any companion services the plugin needs injected (it returns `false` when there is no container, e.g. plain `SchedulerBuilder` usage).

```csharp
public static T UseMyPlugin<T>(this T configurer, Action<MyPluginOptions>? configure = null)
    where T : IPropertyConfigurationRoot
{
    configurer.UsePlugin<MyPlugin>("myPlugin");
    configurer.TryRegisterSingleton<IMyPluginDependency, MyPluginDependency>();
    configure?.Invoke(new MyPluginOptions(configurer));
    return configurer;
}
```

Derive strongly typed options from `PropertiesSetter` with the plugin's property prefix; each setter maps to a `quartz.plugin.{name}.{property}` key applied to the plugin's public setters. The same extension method then works with both `AddQuartz` (constructor injection) and plain `SchedulerBuilder` (reflection, requires a public parameterless constructor).

## Documentation

📖 Full documentation and per-plugin configuration: <https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/quartz-plugins.html>
