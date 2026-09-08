module Quartz.Examples.FSharp.Program

open System.Threading.Tasks

/// Runs both schedulers and waits for each to fire the job once.
let private run () : Task<bool> =
    task {
        let! standalone = Standalone.runGreeting ()
        let! hosted = Hosting.runGreeting ()
        return standalone && hosted
    }

[<EntryPoint>]
let main _ =
    if run().GetAwaiter().GetResult() then
        printfn "Both schedulers fired the job."
        0
    else
        eprintfn "A scheduler did not fire the job inside the timeout."
        1
