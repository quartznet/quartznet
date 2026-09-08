# Quartz.Extensions.DependencyInjection

This package is empty. Everything it used to carry was folded into
[Quartz](https://www.nuget.org/packages/Quartz) in 4.0 — `AddQuartz`, `QuartzOptions`,
`ITriggerConfigurator`, `JobFactoryOptions` and `SchedulingOptions` are all in the `Quartz` package and
the `Quartz` namespace now. Remove this reference, keep `Quartz`, and read the
[migration guide](https://www.quartz-scheduler.net/documentation/quartz-4.x/migration-guide.html) for the
rest of the upgrade.

It is published at every 4.x version for one reason: a dependency bot that updates `Quartz` and this
package as one group resolves that group to the newest version *every* member has. With nothing under this
id above 3.20.1 the group stops there, and the 4.0 pull request the bot had already opened is closed as
superseded. `Quartz.OpenTracing` and `Quartz.OpenTelemetry.Instrumentation` deliberately have no such
package: neither has a 4.x replacement, and an empty one would hide that rather than say it.
