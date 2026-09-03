---

title: Download Quartz.NET
---

Quartz and the packages around it come from [NuGet](https://www.nuget.org/packages/Quartz):

```shell
dotnet add package Quartz
```

That is the whole install for a scheduler — dependency injection, hosting, the health check and
System.Text.Json serialization are in that package. The
[quick start](/documentation/quartz-4.x/quick-start.html) lists the optional packages and what each
one is for.

Each [GitHub release](https://github.com/quartznet/quartznet/releases) also carries a
`Quartz.NET-<version>.zip` with the source, the compiled binaries, the example projects and the
`database/` directory — the table-creation script and the migrations for every supported database.
Those scripts are also readable
[in the repository](https://github.com/quartznet/quartznet/tree/main/database), and what each
migration changes is written up in
[schema changes](/documentation/database/schema-changes.html).
