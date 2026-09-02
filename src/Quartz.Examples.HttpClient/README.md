# Driving a remote scheduler over the HTTP API

A console application that holds no scheduler of its own. `AddQuartzHttpClient` registers an
`IScheduler` that talks to another process's
[HTTP API](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-api.html), so every
call your code already writes against `IScheduler` goes over the wire instead. The page is
[Quartz.HttpClient](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/http-client.html).

## Running it

It needs a server. `Quartz.Examples.AspNetCore` is that server, configured to match — same address,
same API key, same scheduler name — so start it first, in one terminal:

```shell
dotnet run --project src/Quartz.Examples.AspNetCore
```

and this in another:

```shell
dotnet run --project src/Quartz.Examples.HttpClient
```

It waits at a prompt; press enter to read `IScheduler.Status` from the remote scheduler, and type
`exit` to stop. Without the server it prints
`No connection could be made because the target machine actively refused it. (localhost:5000)`, which
is the whole of what "the remote scheduler is not there" looks like.

## What it has to agree with the server about

| | Here | Where the server says it |
|---|---|---|
| Base address | `http://localhost:5000/quartz-api/` | `applicationUrl` in the server's launch profile, plus `QuartzHttpApiOptions.ApiPath` |
| API key header | `X-Quartz-ApiKey: MySuperSecretApiKey` | `QuartzHttpApiKey` in the server's `appsettings.json` |
| Scheduler name | `Quartz ASP.NET Core Sample Scheduler` | `Quartz:Scheduler:InstanceName` in the same file |

The base address is the site root **plus the API path**, and it must end with `/` — the constructor
rejects one that does not. A base address of the site root alone makes every call answer `404`.

`Program.cs` carries three more registrations as comments: constructing an `HttpScheduler` by hand, the
factory overload for a client you build yourself, and registering several remote schedulers, which are
then told apart by the name each is keyed under.
