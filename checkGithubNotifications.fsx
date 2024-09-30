#r "nuget: FsHttp"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"

open System
open System.IO
open FsHttp
open FSharp.Data


let getNotifications () =
    async {

        let! response =
            http {
                GET "https://api.github.com/notifications"
                Accept "application/vnd.github+json"
                UserAgent "rbauduin-test"
                AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                header "X-GitHub-Api-Version" "2022-11-28"
            //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
            }
            |> Request.sendAsync

        let headers = response.headers
        printfn "%A" headers
        printfn "ETag = %A" headers.ETag
        let pollInterval = headers.GetValues("X-Poll-Interval") |> Seq.tryHead
        printfn "pollInterval = %A" pollInterval
        let lastModified = response.content.Headers.LastModified
        printfn "lastModified = %A" lastModified
        let s = response |> Response.toText
        File.WriteAllText("notifications.json", s)
        let json = System.Text.Json.JsonDocument.Parse(s)
        return json
    }

let getNotificationsFromDisk () =
    async {
        let t = File.ReadAllText("notifications.json")
        let json = System.Text.Json.JsonDocument.Parse(t)
        return json
    }




let main () =
    async {
        let! json = getNotifications ()
        //let! json = getNotificationsFromDisk ()
        let user = (json.RootElement[0]?repository?owner?login.ToString())
        let repo = (json.RootElement[0]?repository?name.ToString())
        printfn "New release for %s/%s" user repo
        return 0

    }

main () |> Async.RunSynchronously
