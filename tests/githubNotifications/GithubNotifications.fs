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
open FsUnit

[<SetUp>]
let Setup () = ()

[<Test>]
let test_GetPollInterval () =
    let baseRoute = GET >=> request (fun r -> "body content" |> OK)
    use server = baseRoute >=> setHeader "X-Poll-Interval" "30" |> serve

    let response = http { GET(url "") } |> Request.send

    let pollInterval = getPollInterval response.headers
    pollInterval |> should equal (Some "30")
