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


[<Test>]
let test_getNotificationsFrom () =
    async {
        // Our server just returns the first page of the notifications (doesn't handle per page and page number)
        use _server =
            GET
            >=> choose
                    [ path "/notifications"
                      >=> request (fun _r ->
                          let json = System.IO.File.ReadAllText("fixtures/8pp_p1.json")
                          OK json)
                      path "/not_modified" >=> (fun ctx -> Suave.Redirection.NOT_MODIFIED ctx) ]
            |> serve

        let httpRequest = http { GET(url "/notifications") }
        // We don't mark notificationas as read i nour tests
        let markAsRead = fun _ -> async { return () }
        let lastModified = None

        // We will accumulate the projects we have a notification from in this array
        let mutable acc = [||]

        // Handler accumulating in the array
        let releasesHandler (json: System.Text.Json.JsonElement) =
            task {

                for release in (json.EnumerateArray()) do
                    let user = (release?repository?owner?login.ToString())
                    let repo = (release?repository?name.ToString())
                    acc <- Array.append acc [| sprintf "registering release %s/%s" repo user |]

            }

        // Call function
        do! getNotificationsFrom false httpRequest markAsRead lastModified releasesHandler

        // Check the accumulator was updated accordingly
        acc
        |> should
            equal
            [|

               "registering release AutoBuildImmortalWrt/comengdoc"
               "registering release frontier/aereal"
               "registering release opg-s3-antivirus/ministryofjustice"
               "registering release OpenWrt_x86/mgz0227"
               "registering release pulumi-harbor/pulumiverse"
               "registering release thingino-firmware/CazYokoyama"
               "registering release thingino-firmware/olek87"
               "registering release mason-registry/mason-org" |]


        let notModifiedRequest = http { GET(url "/not_modified") }
        let mutable acc = [||]
        // Handler accumulating in the array
        let releasesHandler (json: System.Text.Json.JsonElement) =
            task {

                for release in (json.EnumerateArray()) do
                    let user = (release?repository?owner?login.ToString())
                    let repo = (release?repository?name.ToString())
                    acc <- Array.append acc [| sprintf "registering release %s/%s" repo user |]

            }
        // Call function
        do! getNotificationsFrom false notModifiedRequest markAsRead lastModified releasesHandler

        // Check the accumulator was updated accordingly
        acc |> should equal [||]
    }

[<Test>]
let test_getNotificationsFromWithLastModifiedHeader () =
    async {
        let markAsRead = fun _ -> async { return () }

        let mutable acc = [||]

        let releasesHandler (json: System.Text.Json.JsonElement) =
            task {

                for release in (json.EnumerateArray()) do
                    let user = (release?repository?owner?login.ToString())
                    let repo = (release?repository?name.ToString())
                    acc <- Array.append acc [| sprintf "registering release %s/%s" repo user |]

            }
        // Taken from FsHttp tests
        let headersToString =
            List.sort
            >> List.map (fun (key, value) -> $"{key}={value}".ToLower())
            >> (fun h -> System.String.Join("&", h))
        // Our server just returns the first page of the notifications (doesn't handle per page and page number)
        let mutable ifModifiedSinceReceivedByServer = []

        use _server =
            GET
            >=> choose
                    [ path "/with_if_modified"
                      >=> request (fun r ->
                          let modified =
                              r.headers
                              |> List.filter (fun (k, _) ->
                                  printfn "Got header %s" k
                                  k.StartsWith("If-Modified-Since", System.StringComparison.OrdinalIgnoreCase))
                              |> List.map (fun (_k, v) -> Some v)

                          printfn "Setting ifModifiedReceivedByServer to %A" modified
                          ifModifiedSinceReceivedByServer <- modified

                          let json = System.IO.File.ReadAllText("fixtures/8pp_p1.json")
                          OK json) ]
            |> serve


        let lastModified = Some System.DateTimeOffset.Now

        let expectedHeader =
            lastModified
            |> Option.map (fun o -> o.DateTime)
            |> Option.get
            |> FSharp.Data.HttpRequestHeaders.IfModifiedSince
            |> snd

        let httpRequest =
            http {
                GET(url "/with_if_modified")

                header
                    "If-Modified-Since"
                    //        (System.DateTime.Parse("2020-01-01")
                    //         |> FSharp.Data.HttpRequestHeaders.IfModifiedSince
                    //         |> snd)

                    (lastModified
                     |> Option.map (fun offset -> offset.DateTime |> FSharp.Data.HttpRequestHeaders.IfModifiedSince)
                     |> Option.map (fun (_h, v) -> v)
                     |> Option.defaultValue (
                         System.DateTime.Parse("2020-01-01")
                         |> FSharp.Data.HttpRequestHeaders.IfModifiedSince
                         |> snd
                     ))

            }
        // Call function
        ifModifiedSinceReceivedByServer <- []
        do! getNotificationsFrom false httpRequest markAsRead lastModified releasesHandler
        ifModifiedSinceReceivedByServer |> should equal [ Some expectedHeader ]


    }
