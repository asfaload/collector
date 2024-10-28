// Script that will look at all events from github.
// It loops continually, waiting for the poll interval sent by github
// to expire before the next iteration.
// It uses the Last-Modified headers to request only new notifications. When no
// change is available, a Not Modified response is returned by github, and it doesn't
// count regarding the requests quota.

open System
open System.IO
open FsHttp
open System.Text.Json
open Asfaload.Collector.DB
open Asfaload.Collector.ChecksumHelpers

FsHttp.Fsi.disableDebugLogs ()

let last_modified_file =
    Environment.GetEnvironmentVariable("NOTIFICATIONS_LAST_MODIFIED_FILE")

let reposWithChecksumsFile =
    Environment.GetEnvironmentVariable("REPOS_WITH_CHECKSUMS_FILE")

let reposWithoutChecksumsFile =
    Environment.GetEnvironmentVariable("REPOS_WITHOUT_CHECKSUMS_FILE")

let mutable reposSeen = List<string>.Empty

let rec getEvents (eventHandler: System.Text.Json.JsonElement -> Async<unit>) =
    async {

        printfn "Start call at %A" DateTime.Now

        let! response =
            http {
                GET "https://api.github.com/events"
                Accept "application/vnd.github+json"
                UserAgent "asfaload-collector"
                AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                header "X-GitHub-Api-Version" "2022-11-28"
            }
            |> Request.sendAsync

        printfn "response code %A" response.statusCode
        let headers = response.headers

        let pollInterval =
            try
                headers.GetValues("X-Poll-Interval") |> Seq.tryHead
            with _e ->
                Some "60"

        printfn "pollInterval = %A" pollInterval

        let nextPollAt =
            pollInterval
            |> Option.map (fun interval -> DateTime.Now + TimeSpan.FromSeconds(float interval))

        // An async sleeper we will wait after we do our work
        let sleeper =
            Async.Sleep(
                pollInterval
                |> Option.map int
                |> Option.map ((*) 1000)
                |> Option.defaultValue 60000
            )
            |> Async.StartAsTask

        if response.statusCode = Net.HttpStatusCode.NotModified then
            printfn "Not modified"
            printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
            do! sleeper |> Async.AwaitTask
            return! getEvents eventHandler
        else if response.statusCode = Net.HttpStatusCode.OK then


            let s = response |> Response.toText
            let json = System.Text.Json.JsonDocument.Parse(s)
            do! eventHandler json.RootElement
            // Now wait until poll interval is passed
            printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
            do! sleeper |> Async.AwaitTask
            return! getEvents eventHandler
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return Unchecked.defaultof<_>
    }




let eventHandler (el: System.Text.Json.JsonElement) =
    async {
        if el.ValueKind = JsonValueKind.Array then
            //let releases = { for event in el.EnumerateArray() when event?``type``="Release"}
            for event in el.EnumerateArray() do
                let fullRepoName = (event?repo?name).ToString()
                printf "looking at %s: " fullRepoName
                // We skip repos named neovim as we encountered a ton of forks
                // with no relevant data
                let user, gitRepo = fullRepoName.Split("/") |> (fun a -> a[0], a[1])
                let! count = Repos.isKnown user gitRepo |> Repos.run
                let seen = count <> 0

                if not seen && not (fullRepoName.EndsWith("/neovim")) then
                    printfn "**NEW**"
                    do! Repos.seen user gitRepo |> Repos.run

                    match! getReleasesForRepo fullRepoName with
                    | NoRelease -> printfn "No release found"
                    | NoChecksum -> printfn "Release found, but no checksum file was found"
                    | Added -> printfn "Added, repo is now tracked"
                    | Known -> printfn "Repo was already known"
                else
                    printfn "-known-"
                    ()
    }

let main () =
    async {

        // If the file exists, it contains the last-modified value for the
        // most recent notification we handled in a previous run.
        let lastModified =
            if File.Exists(last_modified_file) then
                printfn $"Using last modified from file {last_modified_file}"
                Some(File.ReadAllText last_modified_file |> JsonSerializer.Deserialize)
            else
                None

        let queue = Environment.GetEnvironmentVariable("RELEASES_QUEUE")
        // Sleep at restart preventing too rapid requests in case of container restart loop
        do! Async.Sleep 60000
        let! _ = getEvents eventHandler
        return 0
    }

main () |> Async.RunSynchronously |> exit
