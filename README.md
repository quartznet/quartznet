[![Downloads](https://img.shields.io/nuget/dt/Quartz)](#)
[![Build status](https://github.com/quartznet/quartznet/actions/workflows/build.yml/badge.svg)](https://github.com/quartznet/quartznet/actions/workflows/build.yml)
[![NuGet](http://img.shields.io/nuget/v/Quartz.svg)](https://www.nuget.org/packages/Quartz/)
[![NuGet pre-release](http://img.shields.io/nuget/vpre/Quartz.svg)](https://www.nuget.org/packages/Quartz/)
[![Join the chat at https://gitter.im/quartznet/quartznet](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/quartznet/quartznet?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

# Quartz.NET - Enterprise Job Scheduler

Please visit [https://www.quartz-scheduler.net/](https://www.quartz-scheduler.net/) for up to date news and documentation.

## Compatibility

Quartz.NET 4 targets .NET 10, and nothing else. Quartz.NET 3.x, maintained on the
[`3.x` branch](https://github.com/quartznet/quartznet/tree/3.x), supports .NET Standard 2.0 and
.NET Framework 4.6.2 and later.

## Installation

* [Stable builds from NuGet](https://www.nuget.org/packages?q=owner%3AQuartz.NET)
* Pre-release builds from Feedz.io feed: https://f.feedz.io/quartznet/quartznet/nuget/index.json

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

For API documentation, please refer to [Quartz.NET site](https://docs.quartz-scheduler.net/apidoc/3.0/html).
