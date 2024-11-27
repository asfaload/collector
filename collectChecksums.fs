// Script to collect checksums from new releases registered on the Queue by the script
// looking for new notifications.
// It downloads the checksums under a directory hierarchy mimicking the source repo's path to the checksums file,
// including the host, under $BASE_DIR, which needs to be a git repo.
// It looks for checksums files in the release artifacts according to pre-defined patterns.
// When the checksums of a release have been downloaded, a new commit is registered.

open Asfaload.Collector.ChecksumsCollector
open Asfaload.Collector


let rec readQueue () =
    async {

        //do!
        //    Queue.consumeCallbackRelease (fun callbackBody ->
        //        printfn "running callbackRelease %s" (callbackBody.Repository.FullName)
        //        handleCallbackRelease callbackBody |> Async.RunSynchronously)
        //    |> Async.AwaitTask

        //do! Async.Sleep 5000

        do!
            Queue.consumeRepoReleases (fun repo ->
                printfn "repo = %A" repo
                handleRepoRelease repo |> Async.RunSynchronously)
            |> Async.AwaitTask

        gitPushIfAhead ()
        printfn "Fetching release consumed 1 or timed out, will sleep 5s"
        do! Async.Sleep 5000
        return! readQueue ()

    }

let main () =
    async {
        let! _ = readQueue ()
        return 0
    }

main () |> Async.RunSynchronously |> exit
