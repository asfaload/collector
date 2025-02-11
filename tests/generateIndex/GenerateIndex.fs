module tests.GenerateIndex

open Asfaload.Collector.Index
open NUnit.Framework
open FsUnit


open System.IO


[<SetUp>]
let Setup () = ()

[<OneTimeSetUp>]
let OneTimeSetup () =
    System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener())
    |> ignore

[<OneTimeTearDown>]
let OneTimeTearDown () = System.Diagnostics.Trace.Flush()


[<Test>]
let test_getLeafDirectories () =
    let r = getLeafDirectories "./fixtures/getLeafDirectories"

    r
    |> should
        equal
        [| "./fixtures/getLeafDirectories/src/js"
           "./fixtures/getLeafDirectories/src/fsharp/lib" |]
