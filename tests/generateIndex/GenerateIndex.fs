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

[<Test>]
let test_hasHashLength () =
    let md5 = "18cfccc69ddb9947c9abedf87be3127f"
    let sha1 = "a66ba8f482820cc0b4f0e43396f94692ba4d636c"
    let sha256 = "3cb16133432cffbd2433a31a41a65fa4b6ab58ad527d5c6a9cfe8c093a4306fd"

    let sha512 =
        "39a8a7d9c2c3450e39a8161ac6386c6233b92e40725c0ef1efd4b295110a30258d0cf4442c1eb8b7f1d042948714858327ef12ddde66e923275f66f2725f7652"
    // ignore comment lines

    [| md5; sha1; sha256; sha512 |]
    |> Array.iter (fun h -> h |> hasHashLength |> should equal true)

    "other length string" |> hasHashLength |> should equal false
