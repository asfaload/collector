#r "nuget: FsHttp"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"
#r "nuget: DiskQueue, 1.7.1"

open System
open System.IO
open FsHttp
open FSharp.Data
open DiskQueue


let handleRelease (qSession: IPersistentQueueSession) (hoster: string) (user: string) (repo: string) =
    printfn "registering release %s://%s/%s" hoster user repo

    qSession.Enqueue(
        $"{{ hoster : {hoster}, user: {user}, repo: {repo} }}"
        |> System.Text.Encoding.ASCII.GetBytes
    )


let releasesHandler (qSession: IPersistentQueueSession) (json: System.Text.Json.JsonElement) =


    for release in (json.EnumerateArray()) do
        let user = (release?repository?owner?login.ToString())
        let repo = (release?repository?name.ToString())
        handleRelease qSession "github" user repo

    qSession.Flush()

let rec getNotifications (lastModified: DateTimeOffset option) (releasesHandler: System.Text.Json.JsonElement -> unit) =
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


            let s = response |> Response.toText
            let json = System.Text.Json.JsonDocument.Parse(s)
            releasesHandler json.RootElement
            // Now wait until poll interval is passed
            printfn "%A Waiting until next poll at %A" (DateTime.Now) nextPollAt
            do! sleeper |> Async.AwaitTask
            return! getNotifications lastModified releasesHandler
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return Unchecked.defaultof<_>
    }

let main () =
    async {

        // Define queue
        use releasingReposQueue = new PersistentQueue("queues/releasing_repos")
        let qSession = releasingReposQueue.OpenSession()
        let! _ = getNotifications (None) (releasesHandler qSession)
        return 0
    }

main () |> Async.RunSynchronously
