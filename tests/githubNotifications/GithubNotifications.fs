module tests.GithubNotifications

open FsHttp
open FsHttp.Tests.TestHelper
open FsHttp.Tests.Server

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful
open GithubNotifications

open NUnit.Framework
open NUnit
open FsUnit

open System.Diagnostics

[<SetUp>]
let Setup () = ()

[<OneTimeSetUp>]
let OneTimeSetup () =
    System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener())
    |> ignore

[<OneTimeTearDown>]
let OneTimeTearDown () = System.Diagnostics.Trace.Flush()


[<Test>]
let test_GetPollInterval () =
    use _server =
        GET
        >=> choose
                [ path "/set_to_30" >=> setHeader "X-Poll-Interval" "30" >=> OK "body content"
                  path "/not_set" >=> OK "body content" ]
        |> serve

    // Header is set
    let response_30 = http { GET(url "/set_to_30") } |> Request.send
    getPollInterval response_30.headers |> should equal (Some "30")

    // Header is not set, default value
    let response_unset = http { GET(url "/not_set") } |> Request.send
    getPollInterval response_unset.headers |> should equal (Some "60")
