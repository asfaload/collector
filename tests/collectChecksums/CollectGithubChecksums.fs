module tests.CollectGithubChecksums

open FsHttp
open FsHttp.Tests.TestHelper
open FsHttp.Tests.Server

open Asfaload.Collector.ChecksumsCollector

open NUnit.Framework
open NUnit
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
