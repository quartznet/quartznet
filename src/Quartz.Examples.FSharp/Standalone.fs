module Quartz.Examples.FSharp.Standalone

open System
open System.Threading.Tasks

open Quartz

/// A scheduler with no application container: QuartzSchedulerBuilder creates one, and the factory it
/// hands back owns it. This is what a 3.x StdSchedulerFactory() becomes.
let buildFactory () : StandaloneSchedulerFactory =
    QuartzSchedulerBuilder
        .Create(fun q ->
            q
                .ConfigureScheduler(fun options -> options.InstanceName <- "fsharp-example")
                .UseInMemoryStore()
            |> ignore)
        .Build()

/// The job and the trigger that fires it once, straight away.
let describeGreeting () : IJobDetail * ITrigger =
    let job =
        JobBuilder
            .Create<GreetJob>()
            .WithIdentity("greet", "fsharp")
            .Build()

    let trigger =
        TriggerBuilder
            .Create()
            .WithIdentity("greet-now", "fsharp")
            .StartNow()
            .Build()

    job, trigger

/// ScheduleJob is an overload set F# will not resolve from an unannotated argument, because every
/// candidate's trailing parameters are optional and so every candidate matches two arguments.
/// Annotating the trigger is the whole fix.
let scheduleOne (scheduler: IScheduler) (job: IJobDetail) (trigger: ITrigger) : Task<DateTimeOffset> =
    task {
        return! scheduler.ScheduleJob(job, trigger)
    }

/// A ValueTask inside task { }: awaited like any other awaitable, with nothing to convert.
let start (scheduler: IScheduler) : Task<unit> =
    task {
        do! scheduler.Start()
    }

/// The same ValueTask from async { }: Async.AwaitTask has no ValueTask overload, so ask the ValueTask
/// for a Task first — once, which is all a ValueTask allows.
let stop (scheduler: IScheduler) : Async<unit> =
    async {
        do! scheduler.Shutdown(waitForJobsToComplete = true).AsTask() |> Async.AwaitTask
    }

/// Everything above in order: build, schedule, start, wait for one firing, shut down. F# has no
/// `await using`, so the factory that owns the container is disposed by hand.
let runGreeting () : Task<bool> =
    task {
        let factory = buildFactory ()
        let! scheduler = factory.GetScheduler()

        let job, trigger = describeGreeting ()
        let! firstFireTime = scheduleOne scheduler job trigger
        printfn "greet is scheduled for %O" firstFireTime

        do! start scheduler
        let! fired = Firings.waitForOne (TimeSpan.FromSeconds 30.0)
        do! Async.StartAsTask(stop scheduler)
        do! factory.DisposeAsync()

        return fired
    }
