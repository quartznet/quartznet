---

title: Migration Guide
---

*This document outlines changes needed per version upgrade basis. You need to check the steps for each version you are jumping over. You should also check [the complete change log](https://raw.github.com/quartznet/quartznet/master/changelog.md).*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)
:::

## Migrating from Version 3.x to 4.x

### Tasks changed to ValueTask

In a majority of interfaces that previously returned or took a `Task` or `Task<T>` parameter, have been changed to a `ValueTask` or `ValueTask<T>`

In most cases, all you will need to do is adjust the signature from a `Task` to be a `ValueTask`

::: info
Note the following restrictions when working with ValueTask:
:::

> The following operations should never be performed on a `ValueTask<TResult>` instance:
>
> * Awaiting the instance multiple times.
> * Calling AsTask multiple times.
> * Using `.Result` or `.GetAwaiter().GetResult()` when the operation hasn't yet completed, or using them multiple times.
> * Using more than one of these techniques to consume the instance.

For example, to migrate jobs:

```csharp
public async Task Execute(IJobExecutionContext context)
```

becomes:

```csharp
public async ValueTask Execute(IJobExecutionContext context)
```

For more information on `ValueTasks` please see [Microsoft](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-7.0)

### Logging

LibLog has been replaced with the Microsoft.Logging.Abstraction library.
Reconfigure logging using a ILoggerFactory, an example, with a Microsoft.Logging.SimpleConsole logger:

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddSimpleConsole();
    });
LogProvider.SetLogProvider(loggerFactory);
```

See the Quartz.Examples project for examples on setting up [Serilog](https://serilog.net/) and Microsoft.Logging with Quartz.

An alternative approach is to configure the LoggerFactory via a HostBuilder ConfigureServices wire-up:

```csharp
Host.CreateDefaultBuilder(args)
.ConfigureServices((hostContext, services) =>
{
  services.AddQuartz(q =>
        {
          q.SetLoggerFactory(loggerFactory);
        });
});
```

Further information on configuring Microsoft.Logging can be found [at Microsoft docs](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging?tabs=command-line)

### JSON Serialization

To configure JSON serialization to be used in job store instead of old `UseJsonSerializer` you should now use either `UseSystemTextJsonSerializer` or `UseNewtonsoftJsonSerializer`
and remove the old package reference `Quartz.Serialization.Json` (and if Newtonsoft used, reference `Quartz.Serialization.Newtonsoft`). Change was made to distinguish the two common
serializers that are being used (System.Text.Json and JSON.NET).
