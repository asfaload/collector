namespace Rest

open FSharp.Data
open FsHttp
open System

module Notification =
    [<Literal>]
    let notificationsSample = __SOURCE_DIRECTORY__ + @"/samples/notification.json"

    type NotificationData = JsonProvider<notificationsSample>

module Release =
    [<Literal>]
    let releaseSample = __SOURCE_DIRECTORY__ + @"/samples/release.json"

    type ReleaseData = JsonProvider<releaseSample>

    let getLastRelease (user: string) (repo: string) =
        async {

            let ua =
                System.Environment.GetEnvironmentVariable("GH_USER_AGENT")
                |> Option.ofObj
                |> Option.defaultValue "rbauduin-test"

            let! response =
                http {
                    GET $"https://api.github.com/repos/{user}/{repo}/releases/latest"
                    Accept "application/vnd.github+json"
                    UserAgent ua
                    AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                    header "X-GitHub-Api-Version" "2022-11-28"
                //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
                }
                |> Request.sendAsync

            return response.ToText() |> ReleaseData.Parse
        }

    let getRelease (url: System.Uri) =
        async {

            let ua =
                System.Environment.GetEnvironmentVariable("GH_USER_AGENT")
                |> Option.ofObj
                |> Option.defaultValue "rbauduin-test"

            let! response =
                http {
                    GET url.OriginalString
                    Accept "application/vnd.github+json"
                    UserAgent ua
                    AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                    header "X-GitHub-Api-Version" "2022-11-28"
                //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
                }
                |> Request.sendAsync

            return response.ToText() |> ReleaseData.Parse
        }

module Repo =
    [<Literal>]
    let repoSample = __SOURCE_DIRECTORY__ + @"/samples/repo.json"

    type RepoData = JsonProvider<repoSample>

    let getRepo (url: System.Uri) =
        async {

            let ua =
                System.Environment.GetEnvironmentVariable("GH_USER_AGENT")
                |> Option.ofObj
                |> Option.defaultValue "rbauduin-test"

            let! response =
                http {
                    GET url.OriginalString
                    Accept "application/vnd.github+json"
                    UserAgent ua
                    AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                    header "X-GitHub-Api-Version" "2022-11-28"
                //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
                }
                |> Request.sendAsync

            return response.ToText() |> RepoData.Parse
        }
