[![Downloads](https://img.shields.io/nuget/dt/Quartz)](#)
[![Build status](https://github.com/quartznet/quartznet/actions/workflows/build.yml/badge.svg)](https://github.com/quartznet/quartznet/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/Quartz.svg)](https://www.nuget.org/packages/Quartz/)
[![Discussions](https://img.shields.io/github/discussions/quartznet/quartznet)](https://github.com/quartznet/quartznet/discussions)

# Quartz.NET - Enterprise Job Scheduler

Please visit [https://www.quartz-scheduler.net/](https://www.quartz-scheduler.net/) for up to date news and documentation.

## Compatibility

Quartz.NET 4 targets .NET 10, and nothing else. Quartz.NET 3.x, maintained on the
[`3.x` branch](https://github.com/quartznet/quartznet/tree/3.x), supports .NET Standard 2.0 and
.NET Framework 4.6.2 and later.

## Installation

```shell
dotnet add package Quartz
```

That is the whole install for a scheduler — dependency injection, hosting, the health check and
System.Text.Json serialization are in that package. The
[quick start](https://www.quartz-scheduler.net/documentation/quartz-4.x/quick-start.html) lists the
optional packages and what each one is for.

* [Every released package on NuGet](https://www.nuget.org/packages?q=owner%3AQuartz.NET)
* Preview builds of unreleased `main`, pushed on every commit, from the Feedz.io feed:
  https://f.feedz.io/quartznet/quartznet/nuget/index.json

## Questions and Discussion

[GitHub Discussions](https://github.com/quartznet/quartznet/discussions) is where questions are asked
and answered; [issues](https://github.com/quartznet/quartznet/issues) are for bugs and feature
requests.

## Building

* You need the .NET 10 SDK. `global.json` asks for 10.0.100 and rolls forward to the latest 10.0.x you
  have installed.
* Build the code by running `build.cmd` (Windows) or `./build.sh` (Linux, macOS). The scripts restore the
  [Fallout](https://fallout.build/) CLI from `.config/dotnet-tools.json` and hand it the arguments, so
  there is nothing to install globally.
* `build.cmd Compile UnitTest` compiles and runs the unit tests. The integration tests need a running
  Docker daemon; [CONTRIBUTING.md](CONTRIBUTING.md) has the rest.

## License

Licensed under the Apache License, Version 2.0 (the "License"); you may not
use this file except in compliance with the License. You may obtain a copy
of the License [here](http://www.apache.org/licenses/LICENSE-2.0).

For API documentation, please refer to the [Quartz.NET site](https://docs.quartz-scheduler.net/apidoc/3.0/html).
The generated set there is 3.x's; nothing generates a 4.0 one yet.
