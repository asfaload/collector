// Script to collect checksums from new releases registered on the Queue by the script
// looking for new notifications.
// It downloads the checksums under a directory hierarchy mimicking the source repo's path to the checksums file,
// including the host, under $BASE_DIR, which needs to be a git repo.
// It looks for checksums files in the release artifacts according to pre-defined patterns.
// When the checksums of a release have been downloaded, a new commit is registered.

#r "nuget: System.Data.SQLite, 1.0.119"
#load "lib/db.fsx"
#load "lib/Queue.fsx"
#r "nuget: Octokit, 13.0.1"
#load "lib/checksumsCollection.fsx"

open Asfaload.Collector.ChecksumsCollector
open Asfaload.Collector


let rec readQueue () =
    task {
        // This will wait until the queue can be locked.
        do!
            Queue.consumeReleases (fun repo ->
                printfn "repo = %A" repo
                handleRepoRelease repo |> Async.RunSynchronously)

        gitPushIfAhead ()
        printfn "Fetching release consumed 1 or timed out, will sleep 5s"
        do! Async.Sleep 1000
        return! readQueue ()

    }

let main () =
    async {
        let! _ = readQueue () |> Async.AwaitTask
        return 0
    }

main () |> Async.RunSynchronously
