module tests.CollectGithubChecksums

open FsHttp
open FsHttp.Tests.TestHelper
open FsHttp.Tests.Server

open Asfaload.Collector.ChecksumsCollector

open NUnit.Framework
open NUnit
open FsUnit

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful

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
let test_createReleaseDir () =
    let tempDir = Directory.CreateTempSubdirectory().FullName


    // Create a new release dir
    let subDir = "firstDir"
    let expectedDir = Path.Combine(tempDir, subDir)
    let r = createReleaseDir tempDir subDir
    r |> should equal (Some expectedDir)
    Directory.Exists expectedDir |> should equal true

    // Request creation of already existing dir
    let r = createReleaseDir tempDir subDir
    r |> should equal (Some expectedDir)
    Directory.Exists expectedDir |> should equal true

    // Error creating directory
    let expectedDir = "/bin/to/be/deleted"
    let r = createReleaseDir "/bin" "to/be/deleted"
    r |> should equal None
    Directory.Exists expectedDir |> should equal false

[<Test>]
let test_downloadChecksums () =

    use _server =
        GET
        >=> choose
                [ path "/get_checksums" >=> OK "checksums file content"
                  path "/error" >=> Suave.RequestErrors.NOT_FOUND "File not found" ]
        |> serve

    let checksumsUri = System.Uri(url "/get_checksums")

    // Download new file
    let tempDir = Directory.CreateTempSubdirectory().FullName
    let expectedPath = Path.Combine(tempDir, "get_checksums")
    File.Exists(expectedPath) |> should equal false
    let r = downloadChecksums checksumsUri tempDir
    printfn "tempDir = %s" tempDir
    r |> should equal (Some expectedPath)
    File.Exists(expectedPath) |> should equal true

    // Download existing file
    File.Exists(expectedPath) |> should equal true
    let r = downloadChecksums checksumsUri tempDir
    r |> should equal None
    File.Exists(expectedPath) |> should equal true

    // Download error
    let errorURI = System.Uri(url "/error")
    let r = downloadChecksums errorURI tempDir
    r |> should equal None


[<Test>]
let test_getDownloadDir () =
    let downloadUrl =
        "https://github.com/asfaload/asfald/releases/download/v0.5.1/asfald-aarch64-unknown-linux-musl"

    let downloadUri = System.Uri(downloadUrl)
    let r = getDownloadDir "github.com" (downloadUri.Segments)

    r
    |> should equal "github.com/asfaload/asfald/releases/download/v0.5.1/asfald-aarch64-unknown-linux-musl"
