// Script that will look at all events from github.
// It loops continually, waiting for the poll interval sent by github
// to expire before the next iteration.
// It uses the Last-Modified headers to request only new notifications. When no
// change is available, a Not Modified response is returned by github, and it doesn't
// count regarding the requests quota.

open Asfaload.Collector.DB
open Asfaload.Collector.ChecksumHelpers
open PollAll
open FsHttp


let githubEventHandler (targetNumber: int) (el: System.Text.Json.JsonElement) =
    async {
        if el.ValueKind = JsonValueKind.Array then
            // number of new repos seen
            let mutable countNewRepos = 0
            let mutable countKnownRepos = 0
            //let releases = { for event in el.EnumerateArray() when event?``type``="Release"}
            for event in el.EnumerateArray() do

                if countNewRepos < targetNumber then
                    let fullRepoName = (event?repo?name).ToString()
                    printf "looking at %s: " fullRepoName
                    // We skip repos named neovim as we encountered a ton of forks
                    // with no relevant data
                    let user, gitRepo = fullRepoName.Split("/") |> (fun a -> a[0], a[1])
                    let! count = Repos.isKnown user gitRepo |> Sqlite.run
                    let seen = count <> 0

                    if not seen && not (fullRepoName.EndsWith("/neovim")) then
                        printfn "**NEW**"
                        countNewRepos <- countNewRepos + 1
                        do! Repos.seen user gitRepo |> Sqlite.run

                        match! getReleasesForRepo fullRepoName with
                        | NoRelease -> printfn "No release found"
                        | NoChecksum -> printfn "Release found, but no checksum file was found"
                        | Added -> printfn "Added, repo is now tracked"
                        | Known -> printfn "Repo was already known"
                    else
                        printfn "-known-"
                        countKnownRepos <- countKnownRepos + 1
                        ()

            printfn "Saw %d new repos, having seen %d known repos" countNewRepos countKnownRepos
    }


loop (githubEventHandler 30)
