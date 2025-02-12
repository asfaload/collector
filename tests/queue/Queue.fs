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

[<TearDown>]
let TearDown () =
    task {
        // Delete consumers so that all tests starts with no consumer defined
        // This is because work queue do not accept multiple consumers with overlapping filters
        // Delete our consumer so other tests can run
        let! _consumerDeleted = deleteConsumerIfExists "RELEASES" [| "releases.>" |] "test_consumer_config"
        // Delete the consumer so other tests can run without a need to stop the nats server
        let! _consumerDeleted = deleteConsumerIfExists "RELEASES" [| "releases.>" |] "releases_processor"
        ()

    }


[<Test>]
let test_publishToQueue () =
    task {

        let stream = "TEST"
        let subjects = [| "tests.>" |]
        let q = "tests.testPublisToQueue.1"
        do! publishToQueue stream subjects q "my serialise value"
        let! msg = getNextAndAck "TEST" "tests.>" "test_consumer_config" (TimeSpan.FromMilliseconds(1000))
        msg |> should equal (Some "my serialise value")

        // We didn't publish anything else, so nothing back
        let! msg = getNextAndAck "TEST" "tests.>" "test_consumer_config" (TimeSpan.FromMilliseconds(1000))
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
        let! msg = getNextAndAck "RELEASES" "releases.>" "test_consumer_config" (TimeSpan.FromMilliseconds(1000))

        msg
        |> should equal (Some """{"kind":{"Case":"Github"},"user":"asfaload","repo":"asfald","checksums":[]}""")

        // We didn't publish anything else, so nothing back
        let! msg = getNextAndAck "RELEASES" "releases.>" "test_consumer_config" (TimeSpan.FromMilliseconds(1000))
        msg |> should equal None

    }

[<Test>]
let test_publishCallbackRelease () =
    task {

        do! publishCallbackRelease "asfaload" "asfald" """{"in_test": "true"}"""

        let! msg =
            getNextAndAck
                "RELEASES_CALLBACK"
                "releases_callback.>"
                "test_consumer_config"
                (TimeSpan.FromMilliseconds(1000))

        msg |> should equal (Some """{"in_test": "true"}""")

        // We didn't publish anything else, so nothing back
        let! msg =
            getNextAndAck
                "RELEASES_CALLBACK"
                "releases_callback.>"
                "test_consumer_config"
                (TimeSpan.FromMilliseconds(1000))

        msg |> should equal None
    }

[<Test>]
let test_publishRepoReleaseAndItsConsumer () =
    task {

        let repo: Repo =
            { repo = "asfald"
              user = "asfaload"
              kind = Github
              checksums = [] }

        do! publishRepoRelease repo

        let mutable acc: string array = [||]

        do! consumeRepoReleases (fun repo -> acc <- (Array.append acc [| sprintf "%s/%s" repo.user repo.repo |]))

        acc |> should equal [| "asfaload/asfald" |]

        // Remove consumer so we can check the work queue is now empty
        // This is because work queue do not accept multiple consumers with overlapping filters
        let! _consumerDeleted = deleteConsumerIfExists "RELEASES" [| "releases.>" |] "releases_processor"
        // We didn't publish anything else, so nothing back
        let! msg = getNextAndAck "RELEASES" "releases.>" "test_consumer_config" (TimeSpan.FromMilliseconds(1000))
        msg |> should equal None
    }
