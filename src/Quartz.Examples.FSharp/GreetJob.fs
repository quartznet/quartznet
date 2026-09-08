namespace Quartz.Examples.FSharp

open System
open System.Threading
open System.Threading.Tasks

open Quartz

/// Every firing of GreetJob, counted, so the program can wait for one and then shut down.
module Firings =

    let private signal = new SemaphoreSlim(0)

    /// Recorded by the job on each run.
    let record () = signal.Release() |> ignore

    /// Waits for one firing. False means none arrived inside the timeout.
    let waitForOne (timeout: TimeSpan) : Task<bool> = signal.WaitAsync timeout

/// A job, written the way F# has to write it. C# may leave the trailing cancellation token off an
/// implementation because it has a default; F# counts a C# optional parameter as part of the arity of
/// the member being implemented, so both parameters are spelled out or the override does not match.
type GreetJob() =

    interface IJob with

        member _.Execute(context: IJobExecutionContext, cancellationToken: CancellationToken) : ValueTask =
            ValueTask(
                task {
                    do! Task.Delay(TimeSpan.FromMilliseconds 1.0, cancellationToken)
                    printfn "GreetJob fired at %O" context.FireTimeUtc
                    Firings.record ()
                }
            )
