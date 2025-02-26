// Script to collect checksums from new releases registered on the Queue by the script
// looking for new notifications.
// It downloads the checksums under a directory hierarchy mimicking the source repo's path to the checksums file,
// including the host, under $BASE_DIR, which needs to be a git repo.
// It looks for checksums files in the release artifacts according to pre-defined patterns.
// When the checksums of a release have been downloaded, a new commit is registered.

open Asfaload.Collector.ChecksumsCollector
open Asfaload.Collector
open Asfaload.Collector.Ignore

open System

let mininmumTimeBetweenReleases =
    System.Environment.GetEnvironmentVariable("TIME_BETWEEN_RELEASES_MINUTES")
    |> Option.ofObj
    |> Option.map int
    |> Option.defaultValue 30

let mutable reposSeen: Map<string, DateTimeOffset> = Map.empty

let rec readQueue () =
    async {

        printfn "Start of readQueue"

        do!
            Queue.consumeCallbackRelease (fun callbackBody ->
                printfn "running callbackRelease %s" (callbackBody.Repository.FullName)
                handleCallbackRelease callbackBody |> Async.RunSynchronously)
            |> Async.AwaitTask

        do!
            Queue.consumeRepoReleases (fun repo ->
                printfn "user = %s , repo = %s" repo.user repo.repo
                let mapKey = $"{repo.user}/{repo.repo}"

                if Map.containsKey mapKey reposSeen then
                    let lastSeen = Map.find mapKey reposSeen

                    if DateTimeOffset.Now.Subtract(lastSeen) < TimeSpan.FromMinutes(mininmumTimeBetweenReleases) then
                        printfn
                            "ignoring repo %s as it was seen in the last %d minutes"
                            mapKey
                            mininmumTimeBetweenReleases
                    else if isGithubIgnored repo.user repo.repo then
                        printfn "Not collecting checksums of ignored repo %s/%s" repo.user repo.repo
                    else
                        reposSeen <- Map.add mapKey DateTimeOffset.Now reposSeen
                        handleRepoRelease repo |> Async.RunSynchronously
                else
                    reposSeen <- Map.add mapKey DateTimeOffset.Now reposSeen
                    handleRepoRelease repo |> Async.RunSynchronously)
            |> Async.AwaitTask

        gitPushIfAhead ()

        return! readQueue ()

    }

let main () =
    async {
        let! _ = readQueue ()
        return 0
    }

main () |> Async.RunSynchronously |> exit
