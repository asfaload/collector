module PollAll

open System
open System.IO
open FsHttp
open System.Text.Json

let last_modified_file =
    Environment.GetEnvironmentVariable("NOTIFICATIONS_LAST_MODIFIED_FILE")

let reposWithChecksumsFile =
    Environment.GetEnvironmentVariable("REPOS_WITH_CHECKSUMS_FILE")

let reposWithoutChecksumsFile =
    Environment.GetEnvironmentVariable("REPOS_WITHOUT_CHECKSUMS_FILE")

let mutable reposSeen = List<string>.Empty

let rec getEventsNumber (number: int) (eventHandler: System.Text.Json.JsonElement -> Async<unit>) =
    async {

        printfn "Start call at %A" DateTime.Now

        let! response =
            http {
                GET $"https://api.github.com/events?per_page={number}"
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
            return! getEventsNumber number eventHandler
        else if response.statusCode = Net.HttpStatusCode.OK then


            let s = response |> Response.toText
            let json = System.Text.Json.JsonDocument.Parse(s)

            if json.RootElement.ValueKind = JsonValueKind.Array then
                do! eventHandler json.RootElement
            else
                printfn "Json element was not an array!"
            // Now wait until poll interval is passed
            printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
            do! sleeper |> Async.AwaitTask
            return! getEventsNumber number eventHandler
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return Unchecked.defaultof<_>
    }

let getEvents handler = getEventsNumber 50 handler

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

        let queue = Environment.GetEnvironmentVariable("RELEASES_QUEUE")
        // Sleep at restart preventing too rapid requests in case of container restart loop
        do! Async.Sleep 60000
        let! _ = getEvents handler
        return 0
    }

let rec loop handler =
    try
        let _exitStatus = main handler |> Async.RunSynchronously
        ()
    with e ->
        printfn "%s:\n%s" e.Message e.StackTrace

    loop handler
