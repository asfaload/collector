namespace Asfaload.Collector

open Asfaload.Collector.DB
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open FsHttp
open System

[<JsonFSharpConverter>]
type RepoKind =
    | Github
    | Gitlab

[<JsonFSharpConverter>]
type Repo =
    { kind: RepoKind
      user: string
      repo: string
      checksums: string list }

open FSharp.Data

type JwtPayload = JsonProvider<"utils/ReleaseActionJwtPayloadSample.json">
type ReleaseCallbackBody = JsonProvider<"utils/ReleaseActionBodySample.json">


type ReleaseInfo =
    | OctokitRelease of Octokit.Release
    | CallbackRelease of ReleaseCallbackBody.Release

module Config =
    let githubUserAgent =
        System.Environment.GetEnvironmentVariable("GITHUB_USER_AGENT")
        |> Option.ofObj
        |> Option.defaultValue "asfaload-collector"

module ChecksumHelpers =

    let CHECKSUMS =
        [ "checksum.txt"
          "checksums.txt"
          "shasum.txt"
          "SHASUMS256"
          "SHASUMS512"
          "sha256"
          "sha512" ]

    type AdditionStatus =
        | HasChecksum
        | NoChecksum
        | NoRelease

    let checkChecksuminRelease (repo: string) (releaseId: int64) =
        async {
            let! response =
                http {
                    GET $"https://api.github.com/repos/{repo}/releases/{releaseId}/assets"
                    Accept "application/vnd.github+json"
                    UserAgent Config.githubUserAgent
                    AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                    header "X-GitHub-Api-Version" "2022-11-28"
                //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
                }
                |> Request.sendAsync

            let json = response |> Response.toJson
            // Avoid secundary rate limits
            do! Async.Sleep 1000


            let releases = json.GetList()

            if releases |> List.length > 0 then
                let hasChecksums =
                    releases
                    |> List.exists (fun a ->
                        CHECKSUMS
                        |> List.exists (fun chk -> Regex.IsMatch(a?name.ToString(), chk, RegexOptions.IgnoreCase)))

                if hasChecksums then
                    return HasChecksum

                else
                    //printfn "----- https://github.com/%s has a release without artifact!" repo
                    return NoChecksum
            else
                return NoRelease
        }



    let getReleasesForRepo (repo: string) =
        async {
            let! response =
                http {
                    GET $"https://api.github.com/repos/{repo}/releases"
                    Accept "application/vnd.github+json"
                    UserAgent Config.githubUserAgent
                    AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                    header "X-GitHub-Api-Version" "2022-11-28"
                }
                |> Request.sendAsync

            let json = response |> Response.toJson
            do! Async.Sleep 1000

            if json.ValueKind = JsonValueKind.Array && json.GetArrayLength() > 0 then
                //for release in response.EnumerateArray() do
                //    printfn "Got releases %A for repo %s" (release?url) repo
                let lastRelease = json.GetList() |> List.head
                return! checkChecksuminRelease repo ((lastRelease?id).GetInt64())
            else
                return NoRelease
        }

    let filterChecksums (s: seq<string>) =
        s
        |> Seq.filter (fun assetName ->
            CHECKSUMS
            |> List.exists (fun chk -> Regex.IsMatch(assetName, chk, RegexOptions.IgnoreCase)))
        |> Seq.toList
