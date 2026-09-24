---

title: Plugins
---

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides ready-made scheduler plugins: scheduling
jobs from a file at startup, and logging job and trigger history. A plugin implements `ISchedulerPlugin`, in the
`Quartz.Extensibility` namespace.

The plugins are in the `Quartz.Plugins.History`, `Quartz.Plugins.Json` and `Quartz.Plugins.Xml` namespaces,
matching the assembly and package name. In 3.x they were the singular `Quartz.Plugin.*`; a
`quartz.plugin.<name>.type` with the old spelling still resolves, with a warning.

## Installation

```shell
dotnet add package Quartz.Plugins
```

## Configuration

Add and configure each plugin with its extension method. The 3.x flat keys,
`quartz.plugin.{name-to-refer-with}.{property}`, still work.

| Plugin | Extension | Options |
|---|---|---|
| `LoggingJobHistoryPlugin` | `UseJobHistoryLogging(…)` | `JobHistoryLoggingOptions` |
| `LoggingTriggerHistoryPlugin` | `UseTriggerHistoryLogging(…)` | `TriggerHistoryLoggingOptions` |
| `StructuredLoggingJobHistoryPlugin` | `UseStructuredJobLogging(…)` | `JobHistoryLoggingOptions` |
| `StructuredLoggingTriggerHistoryPlugin` | `UseStructuredTriggerLogging(…)` | `TriggerHistoryLoggingOptions` |
| `JsonSchedulingDataProcessorPlugin` | `UseJsonSchedulingConfiguration(…)` | `FileSchedulingOptions` |
| `XmlSchedulingDataProcessorPlugin` | `UseXmlSchedulingConfiguration(…)` | `FileSchedulingOptions` |

The extensions are on `IQuartzBuilder`, so they work under `AddQuartz` and inside
`QuartzSchedulerBuilder.Create(q => …)`. How a plugin is registered and named is in the
[configuration reference](../configuration/reference.md#listeners-calendars-and-plugins).

`FileSchedulingOptions`, for the two schedule-file plugins:

| Option | Type | Default | Description |
|---|---|---|---|
| `Files` | `List<string>` | empty | Files to read the schedule from; get-only, so add to it |
| `FailOnFileNotFound` | bool | **`true`** | A missing file stops the scheduler from starting |
| `FailOnSchedulingError` | bool | `false` | `false`: contents that cannot be scheduled are logged and skipped; `true`: startup fails |
| `ScanInterval` | TimeSpan | `00:00:00` | How often files are re-read; **zero means once**, at startup |

With `ScanInterval` zero, a changed file needs a restart.

`JobHistoryLoggingOptions` and `TriggerHistoryLoggingOptions` hold only message templates, each defaulting to
`null` (the plugin's own). [LoggingJobHistoryPlugin](#loggingjobhistoryplugin) shows their shape.

These options types are the scheduler's named options, so a configuration section binds onto them:

<!-- snippet: sample_plugins_options_from_configuration -->
```csharp
// A plugin's options are the scheduler's own named options, so a configuration section binds
// onto them like any other. The callback below is applied over whatever the section said.
services.Configure<FileSchedulingOptions>(configuration.GetSection("Quartz:Json"));

services.AddQuartz(q => q.UseJsonSchedulingConfiguration(x => x.ScanInterval = TimeSpan.FromMinutes(1)));
```
<!-- endSnippet -->

Sources apply in this order, the last winning:

1. the flat `quartz.plugin.{name}.{property}` keys;
2. values bound onto the options;
3. the callback passed to the extension method.

The callback overrides only the settings it sets. Under `AddQuartz("name", …)` the options are that scheduler's:
bind them with `services.Configure<TOptions>("name", section)`.

## Features

### LoggingJobHistoryPlugin

Logs every job execution and execution veto to the configured logging infrastructure.
`LoggingTriggerHistoryPlugin` does the same for trigger firings, misfires and completions.

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

Both use index-based placeholders. Prefer the structured plugins below unless you have existing message
templates to keep.

### StructuredLoggingJobHistoryPlugin

The structured alternative to `LoggingJobHistoryPlugin`. It uses named template parameters (such as `{JobName}`,
`{TriggerGroup}`) instead of index-based placeholders, so structured sinks such as Serilog and NLog can use the
output. It also avoids the template cache memory leaks the original plugin can cause.

Parameters are mapped by position: a customized template must keep them in the default order.

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
Recommended over `LoggingJobHistoryPlugin` with structured logging providers (Serilog, NLog, etc.).
:::

### StructuredLoggingTriggerHistoryPlugin

The structured alternative to `LoggingTriggerHistoryPlugin`: logs trigger firings, misfires and completions with
named template parameters. Parameters are mapped by position: a customized template must keep them in the default
order.

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
Recommended over `LoggingTriggerHistoryPlugin` with structured logging providers (Serilog, NLog, etc.).
:::

### JsonSchedulingDataProcessorPlugin

Loads jobs and triggers from JSON files when the scheduler initializes, and can re-scan the files for changes.
JSON is the maintained scheduling-file format: it gains each new trigger shape, while
[the XML trigger kinds are frozen](#the-xml-trigger-kinds-are-frozen) at three. Every trigger setting has a
field, including the [preferred node](../tutorial/node-affinity.md); see
[Common Trigger Fields](../configuration/json.md#common-trigger-fields).

::: warning
Periodic scanning for file changes is not supported in a clustered environment.
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
        x.FailOnSchedulingError = true;
    });
});
```
<!-- endSnippet -->

For one file, read once, pass just the file name:

```csharp
services.AddQuartz(q => q.UseJsonSchedulingConfiguration("quartz_jobs.json"));
```

The shorthand adds to `Files`, so it combines with the callback form and with itself.
`UseXmlSchedulingConfiguration` has the same pair.

The file format and trigger types are in [JSON Configuration](../configuration/json.md).

### XmlSchedulingDataProcessorPlugin

The XML twin of `JsonSchedulingDataProcessorPlugin`: loads jobs and triggers from XML files at initialization and
can re-scan them, with the same settings. The format is [frozen at three trigger kinds](#the-xml-trigger-kinds-are-frozen).

<!-- snippet: sample_plugins_xml_scheduling -->
```csharp
services.AddQuartz(q =>
{
    q.UseXmlSchedulingConfiguration(x =>
    {
        x.Files.Add("~/quartz_jobs.config");
        x.ScanInterval = TimeSpan.FromMinutes(1);
        x.FailOnSchedulingError = true;
    });
});
```
<!-- endSnippet -->

::: warning
Periodic scanning for file changes is not supported in a clustered environment.
:::

A file that declares the same job or trigger key (name **and** group) twice is rejected, whatever its
`<processing-directives>`. `<overwrite-existing-data>` and `<ignore-duplicates>` govern the file against the
scheduler, not against itself. See [ProcessingDirectives](../configuration/json.md#processingdirectives); it
applies to both formats.

#### The XML trigger kinds are frozen

`job_scheduling_data_2_0.xsd`, the schema for XML scheduling files, declares three trigger kinds: `simple`,
`cron` and `calendar-interval`. It will not gain a fourth.

| To schedule | XML | JSON |
|---|---|---|
| a simple, cron or calendar-interval trigger | `<simple>`, `<cron>`, `<calendar-interval>` | `Simple`, `Cron`, `CalendarInterval` |
| a daily time interval trigger | not expressible, and will not be | `DailyTimeInterval` |
| a [recurrence trigger](../tutorial/recurrencetrigger.md) | not expressible, and will not be | [`Recurrence`](../configuration/json.md#recurrence-trigger) |
| a trigger in an [execution group](../tutorial/execution-groups.md) | `<execution-group>` | `ExecutionGroup` |
| a trigger with a [retry policy](../how-tos/retrying-failed-jobs.md) | `<retry-policy>` | `RetryPolicy` |
| a trigger with a [preferred node](../tutorial/node-affinity.md) | `<preferred-node>` | `PreferredNode` |
| a [continuation](../how-tos/job-continuations.md) (waits for another trigger's firing) | `<continues-after>`, `<continuation-condition>` | `ContinuesAfter`, `ContinuationCondition` |

- **XML scheduling is not deprecated and is not going away in 4.x.** A `quartz_jobs.xml` that worked on 3.x
  works here.
- Write a schedule that needs another trigger kind as JSON. Move an XML schedule to JSON when it needs a shape
  the schema cannot express.

A new trigger kind needs its own parser, misfire vocabulary and schema branch, so it is added to JSON only.
The bottom four rows are settings on an existing trigger, not new kinds, so XML has an optional element for
each. The first three were added in 4.1 and the continuation in 4.2, all between `<calendar-name>` and
`<job-data-map>`. The schema keeps its `2.0` version and its `http://quartznet.sourceforge.net/JobSchedulingData`
namespace, and a file written before these elements existed still validates with the same meaning:

```xml
<trigger>
  <cron>
    <name>nightlyReport</name>
    <job-name>reportJob</job-name>
    <calendar-name>holidays</calendar-name>
    <execution-group>batch</execution-group>
    <retry-policy>fixed;3;00:00:30</retry-policy>
    <preferred-node>production-node-1</preferred-node>
    <continues-after>
      <name>import</name>
      <group>nightly</group>
    </continues-after>
    <continuation-condition>OnFailure|OnCancellation</continuation-condition>
    <cron-expression>0 0 2 * * ?</cron-expression>
  </cron>
</trigger>
```

- **Order matters.** The schema is a sequence, so the five elements go where shown above.
- `<preferred-node>` takes a scheduler instance id, or `*` for
  [an automatic pin](../tutorial/node-affinity.md#auto-pin-mode).
- `<retry-policy>` takes the policy's stored form.
- An unreadable value is refused when the file is read, naming the trigger.

`<continues-after>` names a trigger like `<delete-trigger>` does: a `<name>` and an optional `<group>`
(default `DEFAULT`). The trigger is stored [waiting](../how-tos/job-continuations.md) for that trigger's next
firing instead of being scheduled.

- `<continuation-condition>` lists the outcomes that release the wait, joined with `|`. Default: `OnSuccess`.
- The parent is **named, not resolved when the file is read**. It may be declared later in the same file (the
  file's triggers are stored parent first) or already be in the store.
- A parent in neither place is refused when the file is scheduled, with `ObjectDoesNotExistException`.
- An unknown outcome, or a condition without `<continues-after>`, is refused when the file is read.

### JobInterruptMonitorPlugin — retired

`JobInterruptMonitorPlugin` was removed in 4.0. Use
[`AddJobTimeout(…)`](../tutorial/job-execution-middleware.md#timing-a-job-out) in the core `Quartz` package, a
middleware that needs no `JobDataMap` keys:

<!-- snippet: sample_job_timeout_register -->
```csharp
builder.AddQuartz(q =>
{
    // every job gets five minutes, unless it says otherwise
    q.AddJobTimeout(TimeSpan.FromMinutes(5));

    // or: no scheduler-wide budget, and only the jobs carrying [JobTimeout] are bounded
    q.AddJobTimeout();
});
```
<!-- endSnippet -->

A job sets its own budget with an attribute, like `[DisallowConcurrentExecution]`:

<!-- snippet: sample_job_timeout_attribute -->
```csharp
// Thirty seconds for this job, whatever the scheduler's default is.
[JobTimeout("00:00:30")]
public sealed class ReportJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // Forward the token: a job that never looks at it cannot be stopped by anything, and is simply
        // reported as having timed out once it finally returns.
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
    }
}

// No timeout at all, whatever the scheduler's default is.
[JobTimeout("00:00:00")]
public sealed class NightlyRebuildJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
```
<!-- endSnippet -->

The plugin's `JobDataMap` keys, `"AutoInterruptable"` and `"MaxRunTime"`, are now ignored; delete them from job
and trigger data. See the [migration guide](../migration-guide.md) for before and after, and
[Job Execution Middleware](../tutorial/job-execution-middleware.md#timing-a-job-out) for what a timeout does to
the trigger.

### ShutdownHookPlugin — retired

`ShutdownHookPlugin`, `UseShutdownHook` and `ShutdownHookOptions` were removed in 4.0. The plugin's
`async void` handler on `AppDomain.CurrentDomain.ProcessExit` was never awaited, so the process could exit
mid-`Shutdown`.

Under a host, [the hosted service](hosted-services-integration.md) stops every registered scheduler as part of
the application's shutdown, awaited. `QuartzHostedServiceOptions.WaitForJobsToComplete` replaces `CleanShutdown`:

<!-- snippet: sample_plugins_shutdown_under_a_host -->
```csharp
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

Without a host, shut the scheduler down on the application's own exit path (the end of `Main`, a `Ctrl+C`
handler, a scope's disposal), where the shutdown can be awaited:

<!-- snippet: sample_plugins_shutdown_without_a_host -->
```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create().Build();
IScheduler scheduler = await schedulerFactory.GetScheduler();
await scheduler.Start();

// ... the application runs ...

await scheduler.Shutdown(waitForJobsToComplete: true);
```
<!-- endSnippet -->

## Adding a plugin

`AddPlugin` has the same three shapes as listener registration: the container builds the plugin, you build it,
or it takes options you configure.

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

- The name is how the scheduler refers to the plugin. Some plugins derive persisted job and trigger keys from
  it, so keep it stable across deployments.
- A `quartz.plugin.{name}.*` key configures the plugin with that name, so a plugin added in code can be
  configured from a file.
- Default: the plugin's type name. The shipped plugins use short names (`xml`, `json`, `jobHistory`, …).

Options of the third shape belong to the scheduler they were added to, so two schedulers can add the same plugin
with the same options type and different values. They are named options under the scheduler's name: a plugin on
`services.AddQuartz("reporting", …)` is also configured by `services.Configure<MyPluginOptions>("reporting", …)`.
A plain `services.Configure<MyPluginOptions>(…)` configures the default scheduler's.

| Inject | Use |
|---|---|
| `IOptions<MyPluginOptions>` | A fixed value |
| `IOptionsMonitor<MyPluginOptions>` | Following a reloading configuration source |

On `IOptionsMonitor`, `CurrentValue` is your scheduler's instance, `Get(name)` is the named one, and `OnChange`
fires for your scheduler's options only.

## Authoring plugin configuration extensions

`ISchedulerPlugin` has three members; only `Initialize` is required. `Start` and `Shutdown` default to doing
nothing, for a plugin that does all its work at initialization (attaching a listener, registering a resolver).
Implement them for work that needs a running scheduler, or resources to release on stop.

Give your plugin an extension method on `IQuartzBuilder`, like the built-in ones: take an options object, apply
it to the plugin, and register the plugin under its conventional name.

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

It works wherever an `IQuartzBuilder` does, in both configuration styles:

<!-- snippet: sample_plugins_using_the_extension -->
```csharp
// under a host
services.AddQuartz(q => q.UseMyPlugin(options => options.SomeSetting = "value"));

// standalone, without an application container — the same callback, a different receiver
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q.UseMyPlugin(options => options.SomeSetting = "value"))
    .BuildScheduler();
```
<!-- endSnippet -->

This extension and a `quartz.plugin.myPlugin.someSetting` key configure the same plugin instance, because they
use the same name. The properties are applied to the registered plugin; no second copy is built.
