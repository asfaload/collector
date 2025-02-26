module GithubNotifications

open System
open System.IO
open FsHttp
open FSharp.Data
open System.Text.Json

let last_modified_file =
    Environment.GetEnvironmentVariable("NOTIFICATIONS_LAST_MODIFIED_FILE")

let getPollInterval (headers: System.Net.Http.Headers.HttpResponseHeaders) =
    try
        headers.GetValues("X-Poll-Interval") |> Seq.tryHead
    with _e ->
        Some "60"

let saveLastModifiedToFile (fileLocation: string) (dt: DateTimeOffset) =
    dt
    |> JsonSerializer.Serialize
    |> (fun json ->
        printfn $"registering most recent last-modified to {fileLocation}"

        try
            File.WriteAllText(fileLocation, json)
        with e ->
            printfn "got exception message %s, not registering last modified on disk" e.Message

    )

let readLastModifiedFromFile (fileLocation: string) : DateTimeOffset =
    File.ReadAllText fileLocation |> JsonSerializer.Deserialize

let getAndPersistLastModifiedFromResponseOrFile (response: Response) (fileLocation: string) =
    response.content.Headers.LastModified
    |> (fun n -> if n.HasValue then (Some n.Value) else None)
    |> function
        | Some v ->
            // Register last modified time we saw
            saveLastModifiedToFile fileLocation v
            Some v
        // This should not happen as the reponse is supposed to have the last-modified
        // header. However, just in case, we handle the situation of a response without it.
        | None ->
            // If the file exists, it contains the last-modified value for the
            // most recent notification we handled.
            if File.Exists(fileLocation) then
                printfn "Using last modified from file when request didn't send any"
                Some(readLastModifiedFromFile fileLocation)
            else
                None

let markNotificationsReadUntil (lastModified: DateTimeOffset) =
    printfn "will mark notifications as read"

    async {
        let! response =
            http {
                GET "https://api.github.com/notifications"
                Accept "application/vnd.github+json"
                UserAgent "rbauduin-test"
                AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                header "X-GitHub-Api-Version" "2022-11-28"
                body

                jsonSerialize
                    {| last_read_at = lastModified
                       read = true |}
            }
            |> Request.sendAsync

        printfn "Mark as read response code %A" response.statusCode

    }

let rec getNotificationsFrom
    (recurse: bool)
    (httpRequest: IToRequest)
    (markNotificationsAsRead: DateTimeOffset -> Async<unit>)
    (lastModified: DateTimeOffset option)
    (releasesHandler: System.Text.Json.JsonElement -> System.Threading.Tasks.Task<unit>)
    =
    async {

        printfn "Start call at %A" DateTime.Now

        let! response = httpRequest |> Request.sendAsync

        printfn "response code %A" response.statusCode
        let headers = response.headers

        let pollInterval = getPollInterval headers

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

            if recurse then
                printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
                do! sleeper |> Async.AwaitTask
                return! getNotificationsFrom recurse httpRequest markNotificationsAsRead lastModified releasesHandler
            else
                return Unchecked.defaultof<_>
        else if response.statusCode = Net.HttpStatusCode.OK then
            let lastModified =
                getAndPersistLastModifiedFromResponseOrFile response last_modified_file


            printfn "Last-modified = %A" lastModified
            let s = response |> Response.toText
            let json = System.Text.Json.JsonDocument.Parse(s)
            do! releasesHandler json.RootElement |> Async.AwaitTask
            // Now wait until poll interval is passed
            match lastModified with
            | Some dt -> do! markNotificationsAsRead dt
            | None -> ()


            if recurse then
                printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
                do! sleeper |> Async.AwaitTask
                return! getNotificationsFrom recurse httpRequest markNotificationsAsRead lastModified releasesHandler
            else
                return Unchecked.defaultof<_>
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return Unchecked.defaultof<_>
    }

// This is the function called from running code.
// It passes the right default parameters to another function, which can be more easily tested
let getNotifications
    (lastModified: DateTimeOffset option)
    (releasesHandler: System.Text.Json.JsonElement -> System.Threading.Tasks.Task<unit>)
    =
    let httpRequest =
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
                 |> Option.defaultValue (DateTime.Parse("2020-01-01") |> HttpRequestHeaders.IfModifiedSince |> snd)
                 |> (fun s ->
                     printfn "Getting notifications modified since %s" s
                     s))
        }

    let recurse = true

    getNotificationsFrom recurse httpRequest markNotificationsReadUntil lastModified releasesHandler

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
