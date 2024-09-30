#r "nuget: FsHttp"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"
#r "nuget: DiskQueue, 1.7.1"

open System
open System.IO
open FsHttp
open FSharp.Data
open DiskQueue

let releasingReposQueue = new PersistentQueue("queues/releasing_repos")
let qSession = releasingReposQueue.OpenSession()

let handleRelease (hoster: string) (user: string) (repo: string) =
    printfn "registering release %s://%s/%s" hoster user repo

    qSession.Enqueue(
        $"{{ hoster : {hoster}, user: {user}, repo: {repo} }}"
        |> System.Text.Encoding.ASCII.GetBytes
    )

    qSession.Flush()

let rec getNotifications (lastModified: DateTimeOffset option) =
    async {

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

        let headers = response.headers
        let pollInterval = headers.GetValues("X-Poll-Interval") |> Seq.tryHead
        printfn "pollInterval = %A" pollInterval
        // An async sleeper we will wait after we do our work
        let sleeper =
            Async.Sleep(
                pollInterval
                |> Option.map int
                |> Option.map ((*) 1000)
                |> Option.defaultValue 60000
            )

        if response.statusCode = Net.HttpStatusCode.NotModified then
            printfn "Not modified"
        else if response.statusCode = Net.HttpStatusCode.OK then
            let lastModified =
                response.content.Headers.LastModified
                |> (fun n -> if n.HasValue then (Some n.Value) else None)


            let nextPollAt =
                pollInterval
                |> Option.map (fun interval -> DateTime.Now + TimeSpan.FromSeconds(float interval))

            let s = response |> Response.toText
            let json = System.Text.Json.JsonDocument.Parse(s)
            let user = (json.RootElement[0]?repository?owner?login.ToString())
            let repo = (json.RootElement[0]?repository?name.ToString())
            handleRelease "github" user repo
            // Now wait until poll interval is passed
            do! sleeper
            return! getNotifications lastModified
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return Unchecked.defaultof<_>
    }

let main () =
    async {
        let! _ = getNotifications (None)
        return 0

    }

main () |> Async.RunSynchronously
