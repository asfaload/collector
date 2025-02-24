// Script that will look at all notifications from github.
// It loops continually, waiting for the poll interval sent by github
// to expire before the next iteration.
// It uses the Last-Modified headers to request only new notifications. When no
// change is available, a Not Modified response is returned by github, and it doesn't
// count regarding the requests quota.
// When a new release is available, it sends it on the Queue for another script to
// collect the checksums of the release.

open Asfaload.Collector
open Asfaload.Collector.DB
open Asfaload.Collector.Ignore
open GithubNotifications
open FsHttp

let releasesHandler (json: System.Text.Json.JsonElement) =
    task {

        // Access and lock queue
        // As we `use` is, it gets disposed when becoming out of scope.
        // We cannot keep it open, because it would prevent other processes to access it.
        for release in (json.EnumerateArray()) do
            let user = (release?repository?owner?login.ToString())
            let repo = (release?repository?name.ToString())

            let isIgnored = isGithubIgnored user repo

            if not <| isIgnored then
                let repo =
                    { user = user
                      repo = repo
                      kind = Github
                      checksums = [] }

                printfn "registering release %A://%s/%s" repo.kind repo.user repo.repo
                do! Queue.publishRepoRelease repo
    }

loop releasesHandler
