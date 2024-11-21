module GithubNotifications

open System
open System.IO
open FsHttp
open FSharp.Data
open System.Text.Json

let last_modified_file =
    Environment.GetEnvironmentVariable("NOTIFICATIONS_LAST_MODIFIED_FILE")

let rec getNotifications
    (lastModified: DateTimeOffset option)
    (releasesHandler: System.Text.Json.JsonElement -> System.Threading.Tasks.Task<unit>)
    =
    async {

        printfn "Start call at %A" DateTime.Now

        let! response =
            http {
                GET "https://api.github.com/notifications"
                Accept "application/vnd.github+json"
                UserAgent "rbauduin-test"
                AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                header "X-GitHub-Api-Version" "2022-11-28"
                //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
                header
                    "If-Modified-Since"

                    (lastModified
                     |> Option.map (fun offset -> offset.DateTime |> HttpRequestHeaders.IfModifiedSince)
                     |> Option.map (fun (_h, v) -> v)
                     |> Option.defaultValue (DateTime.Parse("2020-01-01") |> HttpRequestHeaders.IfModifiedSince |> snd))
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
            return! getNotifications lastModified releasesHandler
        else if response.statusCode = Net.HttpStatusCode.OK then
            let lastModified =
                response.content.Headers.LastModified
                |> (fun n -> if n.HasValue then (Some n.Value) else None)
                |> function
                    | Some v ->
                        // Register last modified time we saw
                        v
                        |> JsonSerializer.Serialize
                        |> (fun json ->
                            printfn $"registering most recent last-modified to {last_modified_file}"

                            try
                                File.WriteAllText(last_modified_file, json)
                            with e ->
                                printfn "got exception message %s, not registering last modified on disk" e.Message

                        )

                        Some v
                    // This should not happen as the reponse is supposed to have the last-modified
                    // header. However, just in case, we handle the situation of a response without it.
                    | None ->
                        // If the file exists, it contains the last-modified value for the
                        // most recent notification we handled.
                        if File.Exists(last_modified_file) then
                            printfn "Using last modified from file when request didn't send any"
                            Some(File.ReadAllText last_modified_file |> JsonSerializer.Deserialize)
                        else
                            None


            printfn "Last-modified = %A" lastModified
            let s = response |> Response.toText
            let json = System.Text.Json.JsonDocument.Parse(s)
            do! releasesHandler json.RootElement |> Async.AwaitTask
            // Now wait until poll interval is passed
            printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
            do! sleeper |> Async.AwaitTask
            return! getNotifications lastModified releasesHandler
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return Unchecked.defaultof<_>
    }


let main handler =
    async {

        // If the file exists, it contains the last-modified value for the
        // most recent notification we handled in a previous run.
        let lastModified =
            if File.Exists(last_modified_file) then
                printfn $"Using last modified from file {last_modified_file}"
                Some(File.ReadAllText last_modified_file |> JsonSerializer.Deserialize)
            else
                None

        let! _ = getNotifications lastModified handler
        return 0
    }

let rec loop handler =
    try
        let _exitStatus = main handler |> Async.RunSynchronously
        ()
    with e ->
        printfn "%s:\n%s" e.Message e.StackTrace

    loop handler
