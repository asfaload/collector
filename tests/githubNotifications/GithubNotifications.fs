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

[<Test>]
let test_saveLastModifiedToFile () =
    let dto = System.DateTimeOffset.Parse("2025-01-23 23:45:32+00")
    let cacheFilePath = System.IO.Path.GetTempFileName()
    saveLastModifiedToFile cacheFilePath dto
    let saved = System.IO.File.ReadAllText(cacheFilePath)
    saved |> should equal "\"2025-01-23T23:45:32+00:00\""

[<Test>]
let test_readLastModifiedFromFile () =
    let dto = System.DateTimeOffset.Parse("2025-01-23 23:45:32+00")
    let cacheFilePath = System.IO.Path.GetTempFileName()
    saveLastModifiedToFile cacheFilePath dto
    let read = readLastModifiedFromFile cacheFilePath
    read |> should equal dto


[<Test>]
let test_getAndPersistLastModified () =

    // Value saved in file on disk
    let previousModified = System.DateTimeOffset.Now.AddDays(-1)
    // formatted for the last-modified header:
    let previousModifiedHeader = previousModified.ToString("r")

    // Value set by http server
    let lastModifiedSet = System.DateTime.Now
    let lastModifiedSetHeader = lastModifiedSet.ToString("r")

    // File where our script keeps the last modified value encountered
    let cacheFilePath = System.IO.Path.GetTempFileName()

    // Setup the cache file with the previousModified value
    saveLastModifiedToFile cacheFilePath previousModified

    // Convert the DateTimeOffset option to a string option
    // formatted correctly for the Last-Modified header
    let dateTimeOffsetOptionToHeader (v: System.DateTimeOffset option) =
        v |> Option.map (fun v -> v.ToString("r")) |> Option.get

    // Http server our test will send request to
    use _server =
        GET
        >=> choose
                [ path "/set"
                  >=> setHeader "Last-Modified" (lastModifiedSetHeader)
                  >=> OK "body content"
                  path "/not_set" >=> OK "body content" ]
        |> serve

    // Header is NOT set
    // Keep this test first, otherwise the cache file might be modified!
    let response = http { GET(url "/not_set") } |> Request.send

    getAndPersistLastModifiedFromResponseOrFile response cacheFilePath
    |> dateTimeOffsetOptionToHeader
    |> should equal previousModifiedHeader

    // Header is set
    let response = http { GET(url "/set") } |> Request.send

    getAndPersistLastModifiedFromResponseOrFile response cacheFilePath
    |> dateTimeOffsetOptionToHeader
    |> should equal lastModifiedSetHeader
