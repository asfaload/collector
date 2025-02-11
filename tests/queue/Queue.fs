module tests.Queue

open Asfaload.Collector.Queue

open NUnit.Framework
open FsUnit


open System


[<SetUp>]
let Setup () = ()

[<OneTimeSetUp>]
let OneTimeSetup () =
    System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener())
    |> ignore

[<OneTimeTearDown>]
let OneTimeTearDown () = System.Diagnostics.Trace.Flush()



[<Test>]
let test_publishToQueue () =
    task {

        let stream = "TEST"
        let subjects = [| "tests.>" |]
        let q = "tests.testPublisToQueue.1"
        do! publishToQueue stream subjects q "my serialise value"
        let! msg = getNextAndAck "TEST" [| "tests.>" |] "test_consumer_config" (TimeSpan.FromMilliseconds(1000))
        msg |> should equal (Some "my serialise value")

        // We didn't publish anything else, so nothing back
        let! msg = getNextAndAck "TEST" [| "tests.>" |] "test_consumer_config" (TimeSpan.FromMilliseconds(1000))
        msg |> should equal None
    }
