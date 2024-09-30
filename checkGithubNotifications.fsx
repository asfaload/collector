#r "nuget: FsHttp"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"

open System
open System.IO
open FsHttp
open FSharp.Data


let getNotifications (lastModified: DateTimeOffset option) =
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
        printfn "ETag = %A" headers.ETag
        let pollInterval = headers.GetValues("X-Poll-Interval") |> Seq.tryHead
        printfn "pollInterval = %A" pollInterval

        if response.statusCode = Net.HttpStatusCode.NotModified then
            printfn "Not modified"
            return pollInterval, lastModified, System.Text.Json.JsonDocument.Parse("[]")
        else if response.statusCode = Net.HttpStatusCode.OK then
            let lastModified =
                response.content.Headers.LastModified
                |> (fun n -> if n.HasValue then (Some n.Value) else None)

            printfn "lastModified = %A" lastModified
            let s = response |> Response.toText
            File.WriteAllText("notifications.json", s)
            let json = System.Text.Json.JsonDocument.Parse(s)
            return pollInterval, lastModified, json
        else
            failwithf "Unexpected response status code %A" (response.statusCode)
            return pollInterval, lastModified, System.Text.Json.JsonDocument.Parse("[]")
    }

let getNotificationsFromDisk () =
    async {
        let t = File.ReadAllText("notifications.json")
        let json = System.Text.Json.JsonDocument.Parse(t)
        return Some 60, None, json
    }




let main () =
    async {
        //let! (Some 60, None, json) = getNotificationsFromDisk ()
        let! pollInterval, lastModified, json = getNotifications (None)

        let nextPollAt =
            pollInterval
            |> Option.map (fun interval -> DateTime.Now + TimeSpan.FromSeconds(float interval))

        nextPollAt |> Option.iter (fun pollAt -> printfn "next poll at: %A" pollAt)

        let user = (json.RootElement[0]?repository?owner?login.ToString())
        let repo = (json.RootElement[0]?repository?name.ToString())
        printfn "New release for %s/%s" user repo

        do! Async.Sleep(TimeSpan.FromSeconds(float (pollInterval |> Option.defaultValue "60")))
        let! pollInterval, lastModified, json = getNotifications (lastModified)
        return 0

    }

main () |> Async.RunSynchronously
