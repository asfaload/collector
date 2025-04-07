// Script that will look at all events from github.
// It loops continually, waiting for the poll interval sent by github
// to expire before the next iteration.
// It uses the Last-Modified headers to request only new notifications. When no
// change is available, a Not Modified response is returned by github, and it doesn't
// count regarding the requests quota.

open Asfaload.Collector.DB
open Asfaload.Collector.Ignore
open Asfaload.Collector.ChecksumHelpers
open PollAll
open FsHttp


let githubEventHandler (targetNumber: int) (el: System.Text.Json.JsonElement) =
    async {
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
                let ignored = isGithubIgnored user gitRepo

                if ignored then
                    printfn "ignore %s/%s repo" user gitRepo
                else if not seen then
                    printfn "**NEW**"
                    countNewRepos <- countNewRepos + 1
                    do! Repos.seen user gitRepo |> Sqlite.run

                    match! getReleasesForRepo fullRepoName with
                    | NoRelease -> printfn "No release found"
                    | NoChecksum -> printfn "Release found, but no checksum file was found"
                    | HasChecksum ->
                        printfn "***** https://github.com/%s has a release with checksums!" fullRepoName

                        let! created = Repos.create user gitRepo |> Sqlite.run

                        if created |> List.length = 0 then
                            printfn "but %s/%s was already known" user gitRepo
                        else
                            printfn "and %s/%s has been added to sqlite" user gitRepo
                else
                    printfn "-known-"
                    countKnownRepos <- countKnownRepos + 1
                    ()

        printfn "Saw %d new repos, having seen %d known repos" countNewRepos countKnownRepos
    }

let newReposPerPoll =
    System.Environment.GetEnvironmentVariable("NEW_REPOS_PER_POLL")
    |> Option.ofObj
    |> Option.map int
    |> Option.defaultValue 20

loop (githubEventHandler newReposPerPoll)
