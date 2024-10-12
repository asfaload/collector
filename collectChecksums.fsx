// Script to collect checksums from new releases registered on the DiskQueue by the script
// looking for new notifications.
// It downloads the checksums under a directory hierarchy mimicking the source repo's path to the checksums file,
// including the host, under $BASE_DIR, which needs to be a git repo.
// It looks for checksums files in the release artifacts according to pre-defined patterns.
// When the checksums of a release have been downloaded, a new commit is registered.

#r "nuget: DiskQueue, 1.7.1"
#r "nuget: Octokit, 13.0.1"
#load "lib/checksumsCollection.fsx"

open DiskQueue
open System
open System.Text.Json
open Asfaload.Collector.ChecksumsCollector
open Asfaload.Collector


let rec readQueue (queue: string) =
    async {
        // This will wait until the queue can be locked.
        let releasingReposQueue = PersistentQueue.WaitFor(queue, TimeSpan.FromSeconds(600))

        let qSession = releasingReposQueue.OpenSession()

        let repo =
            qSession.Dequeue()
            |> Option.ofObj
            |> Option.map System.Text.Encoding.ASCII.GetString
            |> Option.map JsonSerializer.Deserialize<Repo>

        match repo with
        | None ->
            qSession.Dispose()
            // Release the queue so writer can access it
            releasingReposQueue.Dispose()
            gitPushIfAhead ()
            printfn "Nothing in queue, will sleep 1s"
            do! Async.Sleep 1000
            return! readQueue queue
        | Some repo ->
            printfn "repo = %A" repo
            // Our cleanup function does w flush of the session
            do! handleRepoRelease (fun () -> qSession.Flush()) repo
            qSession.Dispose()
            // Release the queue so writer can access it
            releasingReposQueue.Dispose()
            // Introduce a delay to avoid secondary rate limits
            do! Async.Sleep 5000
            return! readQueue queue

    }

let main () =
    async {
        let! _ = readQueue (Environment.GetEnvironmentVariable("RELEASES_QUEUE"))
        return 0
    }

main () |> Async.RunSynchronously
