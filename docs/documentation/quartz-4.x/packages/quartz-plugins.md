---

title: Plugins
---

[Quartz.Plugins](https://www.nuget.org/packages/Quartz.Plugins) provides some useful ready-made plugins for your convenience.

Quartz provides an interface (`ISchedulerPlugin`, in the `Quartz.Extensibility` namespace) for plugging-in additional functionality.

The plugins that ship in this package live in the `Quartz.Plugins.*` namespaces — `Quartz.Plugins.History`,
`Quartz.Plugins.Json` and `Quartz.Plugins.Xml`, matching the
assembly and NuGet package name. In 3.x they were the singular `Quartz.Plugin.*`; a `quartz.plugin.<name>.type`
naming the old spelling still resolves, with a warning.
They provide functionality such as auto-scheduling of jobs upon scheduler startup and logging a history of
job and trigger events.

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
| `JsonSchedulingDataProcessorPlugin` | `UseJsonSchedulingConfiguration(…)` | `FileSchedulingOptions` |
| `XmlSchedulingDataProcessorPlugin` | `UseXmlSchedulingConfiguration(…)` | `FileSchedulingOptions` |

They hang off `IQuartzBuilder`, so they work the same under `AddQuartz` and inside
`QuartzSchedulerBuilder.Create(q => …)`. See the
[configuration reference](../configuration/reference.md#listeners-calendars-and-plugins) for how a plugin
is registered and named.

`FileSchedulingOptions` is what the two schedule-file plugins take, and what it does by default is worth
knowing before you set anything:

| Option | Type | Default | Description |
|---|---|---|---|
| `Files` | `List<string>` | empty | The files to read the schedule from. Get-only, so add to it. |
| `FailOnFileNotFound` | bool | **`true`** | A named file that is not there stops the scheduler from starting. |
| `FailOnSchedulingError` | bool | `false` | A file that parses but whose contents cannot be scheduled is logged and skipped. Turn it on to fail startup instead. |
| `ScanInterval` | TimeSpan | `00:00:00` | How often the files are re-read. **Zero means they are read once**, at startup — a change then needs a restart, not merely a save. |

The two history-logging options types — `JobHistoryLoggingOptions` and `TriggerHistoryLoggingOptions` —
carry nothing but message templates, and every one of them defaults to `null`, meaning the plugin's own.
[LoggingJobHistoryPlugin](#loggingjobhistoryplugin) shows the shape they have to have.

An options type in that table is the scheduler's own named options, so a plugin is configurable from
`appsettings.json` like anything else the container builds — bind the section and the values reach the
plugin:

<!-- snippet: sample_plugins_options_from_configuration -->
```csharp
// A plugin's options are the scheduler's own named options, so a configuration section binds
// onto them like any other. The callback below is applied over whatever the section said.
services.Configure<FileSchedulingOptions>(configuration.GetSection("Quartz:Json"));

services.AddQuartz(q => q.UseJsonSchedulingConfiguration(x => x.ScanInterval = TimeSpan.FromMinutes(1)));
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

### JsonSchedulingDataProcessorPlugin

This plugin loads JSON file(s) to add jobs and schedule them with triggers as the scheduler is initialized, and can optionally periodically scan the file for changes. JSON is the maintained scheduling-file format — it is the one that gains a field when a trigger gains one, and [the XML format is frozen](#the-xml-format-is-frozen) at what it can already express. One trigger property is expressible in neither: a [preferred node](../tutorial/node-affinity.md) pins a trigger to a cluster member, which is a deployment's decision rather than a schedule's, so it is set in code or through the [HTTP API](http-api.md). Everything else a trigger carries has a field — see [Common Trigger Fields](../configuration/json.md#common-trigger-fields).

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
        x.FailOnSchedulingError = true;
    });
});
```
<!-- endSnippet -->

For the common case — one file, read once — name the files and nothing else:

```csharp
services.AddQuartz(q => q.UseJsonSchedulingConfiguration("quartz_jobs.json"));
```

The shorthand adds to `Files` rather than replacing it, so it composes with the callback form and with
itself. `UseXmlSchedulingConfiguration` has the same pair.

See [JSON Configuration](../configuration/json.md) for the full JSON file format and trigger type reference.

### XmlSchedulingDataProcessorPlugin

This plugin loads XML file(s) to add jobs and schedule them with triggers as the scheduler is initialized, and can optionally periodically scan the file for changes. It is the XML twin of `JsonSchedulingDataProcessorPlugin` — same surface, same settings, and a format that is frozen.

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
The periodically scanning of files for changes is not currently supported in a clustered environment.
:::

A file that declares the same job or trigger key — name **and** group — twice is rejected, whatever
`<processing-directives>` it carries. `<overwrite-existing-data>` and `<ignore-duplicates>` say how
the file relates to the scheduler, and neither can say anything about how the file relates to itself.
The [ProcessingDirectives](../configuration/json.md#processingdirectives) section has the details; they
apply to both formats.

#### The XML format is frozen

`job_scheduling_data_2_0.xsd`, the schema every XML scheduling file is validated against, is what the
XML format will be for the life of 4.x. It declares three trigger kinds — `simple`, `cron` and
`calendar-interval` — and it will not gain a fourth, nor the trigger settings written since:

| To schedule | XML | JSON |
|---|---|---|
| a simple, cron or calendar-interval trigger | `<simple>`, `<cron>`, `<calendar-interval>` | `Simple`, `Cron`, `CalendarInterval` |
| a daily time interval trigger | not expressible, and will not be | `DailyTimeInterval` |
| a [recurrence trigger](../tutorial/recurrencetrigger.md) | not expressible, and will not be | [`Recurrence`](../configuration/json.md#recurrence-trigger) |
| a trigger with a [retry policy](../how-tos/retrying-failed-jobs.md) | not expressible, and will not be | `RetryPolicy` |
| a trigger in an [execution group](../tutorial/execution-groups.md) | not expressible, and will not be | `ExecutionGroup` |

This is a decision, not a backlog. Two file formats that both grow means two parsers, two schemas and
two sets of documentation for one feature, and the XML one is the one carrying twenty years of files
that must keep loading unchanged. So it keeps loading them: **XML scheduling is not deprecated and is
not going away in 4.x**. A `quartz_jobs.xml` that worked on 3.x works here, and a schedule that only
needs the three trigger kinds above can stay in XML indefinitely. Write a new schedule as JSON, and
move an XML one when it needs something the schema above cannot spell.

### JobInterruptMonitorPlugin — retired

There is no plugin for job timeouts any more. `JobInterruptMonitorPlugin` was removed in 4.0 and
replaced by
[`AddJobTimeout(…)`](../tutorial/job-execution-middleware.md#timing-a-job-out) in the core `Quartz`
package, which is a middleware rather than a plugin and needs no `JobDataMap` keys at all:

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

A job varies the budget by declaring one, the way it declares `[DisallowConcurrentExecution]`:

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

The two `JobDataMap` keys the plugin reserved — `"AutoInterruptable"` and `"MaxRunTime"` — mean nothing
now and can be deleted from job and trigger data. See the
[migration guide](../migration-guide.md) for the before-and-after, and
[Job Execution Middleware](../tutorial/job-execution-middleware.md#timing-a-job-out) for what a timeout
does to the trigger.

### ShutdownHookPlugin — retired

`ShutdownHookPlugin`, `UseShutdownHook` and `ShutdownHookOptions` were removed in 4.0. The plugin
subscribed to `AppDomain.CurrentDomain.ProcessExit` with an `async void` handler, so the shutdown it
started had no one to await it: the process was free to exit mid-`Shutdown`, which is the opposite of
the clean shutdown the plugin's name promised.

Under a host, [the hosted service](hosted-services-integration.md) already does the whole of this —
it stops every registered scheduler as part of the application's own shutdown, awaited, and
`QuartzHostedServiceOptions.WaitForJobsToComplete` is `CleanShutdown` under its real name:

<!-- snippet: sample_plugins_shutdown_under_a_host -->
```csharp
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

A scheduler with no host to stop it shuts itself down on whatever exit path the application already
has — the end of `Main`, a `Ctrl+C` handler, the disposal of a scope — and that path can await the
shutdown, which `ProcessExit` never could:

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

// standalone, without an application container — the same callback, a different receiver
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q.UseMyPlugin(options => options.SomeSetting = "value"))
    .BuildScheduler();
```
<!-- endSnippet -->

Configuration written this way and configuration written as `quartz.plugin.myPlugin.someSetting`
reach the same plugin instance, because they agree on its name: the properties are applied to the
plugin the code registered rather than building a second copy of it.
