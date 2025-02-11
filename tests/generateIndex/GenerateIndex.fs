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

[<Test>]
let test_filterLines () =
    // ignore comment lines
    "# this is a comment line" |> filterLines |> should equal false
    // ignore empty lines
    "" |> filterLines |> should equal false
    // sha256 line
    "3cb16133432cffbd2433a31a41a65fa4b6ab58ad527d5c6a9cfe8c093a4306fd  myfile"
    |> filterLines
    |> should equal true
    // Non-empty line
    "blabla" |> filterLines |> should equal true
