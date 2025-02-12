module tests.Queue

open Asfaload.Collector.Queue
open Asfaload.Collector

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

[<Test>]
let test_publishRepoRelease () =
    task {

        let repo: Repo =
            { repo = "asfald"
              user = "asfaload"
              kind = Github
              checksums = [] }

        do! publishRepoRelease repo
        let! msg = getNextAndAck "RELEASES" [| "releases.>" |] "test_consumer_config" (TimeSpan.FromMilliseconds(1000))

        msg
        |> should equal (Some """{"kind":{"Case":"Github"},"user":"asfaload","repo":"asfald","checksums":[]}""")

        // We didn't publish anything else, so nothing back
        let! msg = getNextAndAck "RELEASES" [| "releases.>" |] "test_consumer_config" (TimeSpan.FromMilliseconds(1000))
        msg |> should equal None
    }

[<Test>]
let test_publishCallbackRelease () =
    task {

        do! publishCallbackRelease "asfaload" "asfald" """{"in_test": "true"}"""

        let! msg =
            getNextAndAck
                "RELEASES_CALLBACK"
                [| "releases_callback.>" |]
                "test_consumer_config"
                (TimeSpan.FromMilliseconds(1000))

        msg |> should equal (Some """{"in_test": "true"}""")

        // We didn't publish anything else, so nothing back
        let! msg =
            getNextAndAck
                "RELEASES_CALLBACK"
                [| "releases_callback.>" |]
                "test_consumer_config"
                (TimeSpan.FromMilliseconds(1000))

        msg |> should equal None
    }
