module Quartz.Examples.FSharp.Hosting

open System
open System.Threading.Tasks

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open Quartz

/// The same scheduler with a container behind it: AddQuartz registers its object graph and
/// AddQuartzHostedService starts and stops it with the host. Each F# lambda becomes the Action<'T>
/// the C# overload asks for, and each fluent call is piped into ignore because F# does not discard a
/// return value for you.
let runGreeting () : Task<bool> =
    task {
        let builder = Host.CreateApplicationBuilder()
        builder.Logging.SetMinimumLevel LogLevel.Warning |> ignore

        builder.Services
            .AddQuartz(fun q ->
                q
                    .UseInMemoryStore()
                    .ScheduleJob<GreetJob>(fun trigger ->
                        trigger.WithIdentity("greet-hosted", "fsharp").StartNow() |> ignore)
                |> ignore)
            .AddQuartzHostedService(fun options -> options.WaitForJobsToComplete <- true)
        |> ignore

        use host = builder.Build()
        do! host.StartAsync()

        let! fired = Firings.waitForOne (TimeSpan.FromSeconds 30.0)

        do! host.StopAsync()
        return fired
    }
