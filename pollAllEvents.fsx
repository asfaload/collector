// Script that will look at all events from github.
// It loops continually, waiting for the poll interval sent by github
// to expire before the next iteration.
// It uses the Last-Modified headers to request only new notifications. When no
// change is available, a Not Modified response is returned by github, and it doesn't
// count regarding the requests quota.
// When a new release is available, it sends it on the DiskQueue for another script to
// collect the checksums of the release.
#load "lib/Shared.fsx"
#r "nuget: FsHttp"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"
#r "nuget: DiskQueue, 1.7.1"
#r "nuget: FsHttp.FSharpData, 14.5.1"
#r "nuget: FSharp.SystemTextJson, 1.3.13"

open System
open System.IO
open FsHttp
open System.Text.Json
open System.Text.RegularExpressions

FsHttp.Fsi.disableDebugLogs ()

let last_modified_file =
    Environment.GetEnvironmentVariable("NOTIFICATIONS_LAST_MODIFIED_FILE")

let reposWithChecksumsFile =
    Environment.GetEnvironmentVariable("REPOS_WITH_CHECKSUMS_FILE")

let reposWithoutChecksumsFile =
    Environment.GetEnvironmentVariable("REPOS_WITHOUT_CHECKSUMS_FILE")

let CHECKSUMS =
    [ "checksum.txt"
      "checksums.txt"
      "SHA256SUMS"
      "SHA256SUMS.txt"
      "SHA512SUMS"
      "SHA512SUMS.txt"
      "SHASUMS256"
      "SHASUMS256.txt"
      "SHASUMS512.txt"
      "SHASUMS512"
      // Neovim's approach:
      ".*\.sha256sum" ]

let mutable reposSeen = List<string>.Empty

let rec getEvents (eventHandler: System.Text.Json.JsonElement -> unit) =
    async {

        printfn "Start call at %A" DateTime.Now

        let! response =
            http {
                GET "https://api.github.com/events"
                Accept "application/vnd.github+json"
                UserAgent "rbauduin-test"
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
            eventHandler json.RootElement
            // Now wait until poll interval is passed
            printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
            do! sleeper |> Async.AwaitTask
            return! getEvents eventHandler
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return Unchecked.defaultof<_>
    }

let checkChecksuminRelease (repo: string) (releaseId: int64) =
    let json =
        http {
            GET $"https://api.github.com/repos/{repo}/releases/{releaseId}/assets"
            Accept "application/vnd.github+json"
            UserAgent "rbauduin-test"
            AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
            header "X-GitHub-Api-Version" "2022-11-28"
        //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
        }
        |> Request.send
        |> Response.toJson


    let releases = json.GetList()

    if releases |> List.length > 0 then
        let hasChecksums =
            releases
            |> List.exists (fun a ->
                CHECKSUMS
                |> List.exists (fun chk ->
                    let regex = Regex(chk)
                    regex.IsMatch(a?name.ToString())))

        if hasChecksums then
            File.AppendAllText(reposWithChecksumsFile, $"https://github.com/{repo}\n")
            printfn "***** https://github.com/%s has a release with checksums!" repo
        else
            File.AppendAllText(reposWithoutChecksumsFile, $"https://github.com/{repo}\n")
            printfn "@@@@@ https://github.com/%s has a release without checksums!" repo
    else
        //printfn "----- https://github.com/%s has a release without artifact!" repo
        ()




let getReleasesForRepo (repo: string) =
    let response =
        http {
            GET $"https://api.github.com/repos/{repo}/releases"
            Accept "application/vnd.github+json"
            UserAgent "rbauduin-test"
            AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
            header "X-GitHub-Api-Version" "2022-11-28"
        }
        |> Request.send
        |> Response.toJson

    if response.ValueKind = JsonValueKind.Array && response.GetArrayLength() > 0 then
        //for release in response.EnumerateArray() do
        //    printfn "Got releases %A for repo %s" (release?url) repo
        let lastRelease = response.GetList() |> List.head
        checkChecksuminRelease repo ((lastRelease?id).GetInt64())
    else
        ()



let eventHandler (el: System.Text.Json.JsonElement) =
    if el.ValueKind = JsonValueKind.Array then
        //let releases = { for event in el.EnumerateArray() when event?``type``="Release"}
        for event in el.EnumerateArray() do
            let repo = (event?repo?name).ToString()

            if not (reposSeen |> List.contains repo) then
                getReleasesForRepo repo
                reposSeen <- List.append reposSeen [ repo ]
            else
                //printfn "%s skipping repo already seen" repo
                ()


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
        let! _ = getEvents eventHandler
        return 0
    }

main () |> Async.RunSynchronously
main () |> Async.RunSynchronously
main () |> Async.RunSynchronously
main () |> Async.RunSynchronously
main () |> Async.RunSynchronously
main () |> Async.RunSynchronously
