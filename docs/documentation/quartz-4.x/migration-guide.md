---
layout: default
title: Version Migration Guide
---

*This document outlines changes needed per version upgrade basis. You need to check the steps for each version you are jumping over. You should also check [the complete change log](https://raw.github.com/quartznet/quartznet/master/changelog.md).*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)
:::

## Migrating from Version 3.x to 4.x

### Logging

LibLog has been replaced with the Microsoft.Logging.Abstraction library.
Reconfigure logging using a ILoggerFactory, an example, with a Microsoft.Logging.SimpleConsole logger:

```c#
 var loggerFactory = LoggerFactory.Create(builder =>
      {
          builder
              .SetMinimumLevel(LogLevel.Debug)
              .AddSimpleConsole();
      });
      LogProvider.SetLogProvider(loggerFactory);
```

See the Quartz.Examples project for examples on setting up Serilog and Microsoft.Logging with Quartz.

An alternative approach is to configure the LoggerFactory via a HostBuilder ConfigureServices wire-up:

```c#

 Host.CreateDefaultBuilder(args)
  .ConfigureServices((hostContext, services) =>
  {
      services.AddQuartz(q =>
            {
              q.SetLoggerFactory(loggerFactory);
            });
  }
```

Further information on configuring Microsoft.Logging can be found [at Microsoft docs](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging?tabs=command-line)