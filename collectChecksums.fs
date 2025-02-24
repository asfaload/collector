// Script to collect checksums from new releases registered on the Queue by the script
// looking for new notifications.
// It downloads the checksums under a directory hierarchy mimicking the source repo's path to the checksums file,
// including the host, under $BASE_DIR, which needs to be a git repo.
// It looks for checksums files in the release artifacts according to pre-defined patterns.
// When the checksums of a release have been downloaded, a new commit is registered.

open Asfaload.Collector.ChecksumsCollector
open Asfaload.Collector
open Asfaload.Collector.Ignore


let rec readQueue () =
    async {

        let mutable handledRepo = false

        do!
            Queue.consumeCallbackRelease (fun callbackBody ->
                handledRepo <- true
                printfn "running callbackRelease %s" (callbackBody.Repository.FullName)
                handleCallbackRelease callbackBody |> Async.RunSynchronously)
            |> Async.AwaitTask

        if handledRepo then
            do! Async.Sleep 1000

        do!
            Queue.consumeRepoReleases (fun repo ->
                printfn "user = %s , repo = %s" repo.user repo.repo

                if isGithubIgnored repo.user repo.repo then
                    printfn "Not collecting checksums of ignored repo %s/%s" repo.user repo.repo
                else
                    handledRepo <- true
                    handleRepoRelease repo |> Async.RunSynchronously)
            |> Async.AwaitTask

        gitPushIfAhead ()
        printfn "Fetching release consumed 1 or timed out, will sleep 5s"

        if handledRepo then
            printfn "sleep 5s as handled repo"
            do! Async.Sleep 5000

        return! readQueue ()

    }

let main () =
    async {
        let! _ = readQueue ()
        return 0
    }

main () |> Async.RunSynchronously |> exit
