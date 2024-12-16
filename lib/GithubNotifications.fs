module GithubNotifications

open System
open System.IO
open FsHttp
open FSharp.Data
open System.Text.Json

let last_modified_file =
    Environment.GetEnvironmentVariable("NOTIFICATIONS_LAST_MODIFIED_FILE")

let notifications_since_file =
    Environment.GetEnvironmentVariable("NOTIFICATIONS_SINCE_FILE")

// This is not so useful as notifications are returned most recent first, and
// here we mark all notif older than X as read.
// Something to try:
// 1. Mark all notifications read until the most recent handled
// 2. Mark all notifications unread until the oldest handled
let markNotificationsReadUntil (lastModified: DateTimeOffset) (read: bool) =
    printfn "will mark notifications as read from %A" lastModified

    async {
        let! response =
            http {
                PUT "https://api.github.com/notifications"
                Accept "application/vnd.github+json"
                UserAgent "frshstff-test"
                AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                header "X-GitHub-Api-Version" "2022-11-28"
                body

                jsonSerialize
                    {| last_read_at = lastModified
                       read = read |}
            }
            |> (fun r ->
                printfn "%s" (r.ToString())
                r)
            |> Request.sendAsync

        printfn "Mark as read response code %A" response.statusCode
        File.WriteAllText("/tmp/last_mark_as_read_response.json", response.ToText())

    }

let rec getNotifications
    (lastModified: DateTimeOffset option)
    (releasesHandler: Rest.Notification.NotificationData.Root -> System.Threading.Tasks.Task<unit>)
    =
    async {

        printfn "Start call at %A" DateTime.Now

        let sinceParam =
            if File.Exists notifications_since_file then
                let since = File.ReadAllText notifications_since_file
                printfn "retrieving unread notifications since %s" since
                Some $"since={since}"
            else
                printfn "no 'since' filter passed"
                None


        let! response =
            http {
                GET $"""https://api.github.com/notifications?{sinceParam |> Option.defaultValue ""}"""
                Accept "application/vnd.github+json"
                UserAgent "frshstff-test"
                AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                header "X-GitHub-Api-Version" "2022-11-28"
                //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
                header
                    "If-Modified-Since"

                    (lastModified
                     // If we have a since, do not include the modified header
                     |> Option.bind (fun offset -> if sinceParam.IsNone then Some offset else None)
                     |> Option.map (fun offset -> offset.DateTime |> HttpRequestHeaders.IfModifiedSince)
                     |> Option.map (fun (_h, v) -> v)
                     |> Option.defaultValue (DateTime.Parse("2020-01-01") |> HttpRequestHeaders.IfModifiedSince |> snd))
            }
            |> (fun r ->
                printfn "%s" (r.ToString())
                r)
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

            // We keep track of the most recent notification we read. This is useful when we miss a bunch of
            // notifications. When we then retrieve notifications, we will not get all notifications, and if we used the
            // last modified value (set as header by the server, and corresponding to the time of the query), we would miss a lot of notifications
            let mutable mostRecentNotification: Option<DateTimeOffset> = None
            let mutable oldestNotification: Option<DateTimeOffset> = None

            for notif in (json.RootElement.EnumerateArray()) do

                let notificationData =
                    (notif.ToString()) |> Rest.Notification.NotificationData.Parse

                // The newest is the first one we get
                if mostRecentNotification.IsNone then
                    mostRecentNotification <- Some notificationData.UpdatedAt
                // The oldest is the last we will handle
                oldestNotification <- Some notificationData.UpdatedAt

                do! releasesHandler notificationData |> Async.AwaitTask

            do! markNotificationsReadUntil (mostRecentNotification |> Option.get) true
            // Now wait until poll interval is passed
            let! effectiveLastModified =
                match lastModified, mostRecentNotification with
                | Some dt, None ->
                    async {
                        printfn "user last modified"
                        return Some dt
                    }
                | None, Some dt ->
                    async {
                        printfn "user notification update time"
                        return Some dt
                    }
                // When we have both, we keep the earliest as the point from which we need to query notifications
                // at the next iteration
                | Some modified, Some read ->
                    async {
                        if read < modified then
                            printfn "using notification update time %A and not modified %A" read modified
                            return Some read
                        else
                            printfn "using modified %A and not notification update time %A" modified read
                            return Some modified
                    }
                | None, None ->
                    async {
                        printfn "no last-modified, so not marking read to that point"
                        return None
                    }

            printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
            do! sleeper |> Async.AwaitTask
            printfn "User last -modified value of %A" effectiveLastModified
            return! getNotifications effectiveLastModified releasesHandler
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
