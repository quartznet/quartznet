# Quartz.Serialization.Json

This package is empty. The Json.NET job store serializer is
[Quartz.Serialization.Newtonsoft](https://www.nuget.org/packages/Quartz.Serialization.Newtonsoft) from 4.0
on — the name says which JSON library it uses, now that the System.Text.Json one is built into
[Quartz](https://www.nuget.org/packages/Quartz) and is the default. Reference
`Quartz.Serialization.Newtonsoft` instead of this, call `UseNewtonsoftJsonSerializer()` as before, and read
the [migration guide](https://www.quartz-scheduler.net/documentation/quartz-4.x/migration-guide.html) for
the rest of the upgrade.

It is published at every 4.x version for one reason: a dependency bot that updates `Quartz` and this
package as one group resolves that group to the newest version *every* member has. With nothing under this
id above 3.20.1 the group stops there, and the 4.0 pull request the bot had already opened is closed as
superseded. `Quartz.OpenTracing` and `Quartz.OpenTelemetry.Instrumentation` deliberately have no such
package: neither has a 4.x replacement, and an empty one would hide that rather than say it.
