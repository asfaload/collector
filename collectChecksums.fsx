#load "lib/Shared.fsx"
#r "nuget: Octokit, 13.0.1"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"
#r "nuget: FsHttp.FSharpData, 14.5.1"

open Octokit
open System
open System.IO
open FsHttp
open FSharp.Data
open FsHttp.FSharpData
open FSharp.Data.JsonExtensions
open Asfaload.Collector



let CHECKSUMS =
    [ "checksum.txt"
      "checksums.txt"
      "SHA256SUMS"
      "SHA256SUMS.txt"
      "SHA512SUMS"
      "SHA512SUMS.txt"
      "SHASUMS256"
      "SHASUMS256.txt"
      "SHASUMS512.txt"
      "SHASUMS512" ]

// Returns Some only if the directory was created.
// If the directory existed or in case of error, returns None
let createReleaseDir (path: string) =
    let baseDir = Environment.GetEnvironmentVariable("BASE_DIR")
    printfn "basedir = %s" baseDir
    // Second path needs to be relative, ot it iss returned as result.....
    let absoluteDirPath = Path.Combine(baseDir, Path.GetRelativePath("/", path))
    printfn "absolute dir path = %s" absoluteDirPath

    if Directory.Exists absoluteDirPath then
        printfn "Directory exists, returning None to stop further processing"
        None
    else
        let dir = Directory.CreateDirectory absoluteDirPath
        if dir.Exists then Some absoluteDirPath else None

let downloadChecksums (checksumsUri: Uri) destinationDir =
    let filename = checksumsUri.Segments |> Array.last
    let filePath = Path.Combine(destinationDir, filename)
    printfn "downloading %s" (checksumsUri.ToString())
    get (checksumsUri.ToString()) |> Request.send |> Response.saveFile filePath
    filePath

let tokenAuth = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
let client = new GitHubClient(new ProductHeaderValue("my-testing-app"))
client.Credentials <- tokenAuth


let downloadIndividualChecksumsFile (lastUri: Uri) (downloadSegments: string array) (filename: string) =
    async {
        let checksumsSegments = Array.append downloadSegments [| filename |]
        let checksumsPath = Path.Join(checksumsSegments)
        let checksumsUri = System.Uri(lastUri, checksumsPath)

        let resultingOption =
            downloadSegments
            |> Path.Combine
            |> createReleaseDir
            |> Option.map (downloadChecksums checksumsUri)


        match resultingOption with
        | Some path -> printfn "New checksums file downloaded at %s" path
        | None -> printfn "No download took place"

    }

let getLastGithubRelease (username: string) (repo: string) =
    async {
        let options = new ApiOptions(PageSize = 3, PageCount = 1)
        let! releases = client.Repository.Release.GetAll(username, repo, options) |> Async.AwaitTask
        let last = releases |> Seq.head
        return last

    }

let downloadLastChecksums (username: string) (repo: string) (checksums: string list) =
    async {
        printfn "Running downloadLast for %s/%s" username repo
        let! last = getLastGithubRelease username repo
        let lastUri = System.Uri(last.HtmlUrl)

        let downloadSegments =
            lastUri.Segments |> Array.map (fun s -> if s = "tag/" then "download/" else s)

        return!
            checksums
            |> List.map (fun name -> downloadIndividualChecksumsFile lastUri downloadSegments name)
            |> Async.Parallel

    }

let updateChecksumsNames (repo: Repo) =
    async {
        let! lastRelease = getLastGithubRelease repo.user repo.repo
        let releaseId = lastRelease.Id

        let! response =
            http {
                GET $"https://api.github.com/repos/{repo.user}/{repo.repo}/releases/{releaseId}/assets"
                Accept "application/vnd.github+json"
                UserAgent "rbauduin-test"
                //AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                header "X-GitHub-Api-Version" "2022-11-28"
            //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
            }
            |> Request.sendAsync

        let json = response |> Response.toJson

        let checksumsFiles =
            json.AsArray()
            |> Array.filter (fun a -> CHECKSUMS |> List.contains (a?name.AsString()))
            |> Array.map (fun a -> a?name.AsString())
            |> Array.toList

        printfn "found checksums files %A" checksumsFiles

        return { repo with checksums = checksumsFiles }
    }


let main () =
    async {
        let repos =
            [ { kind = Github
                user = "jesseduffield"
                repo = "lazygit"
                checksums = [] }
              { kind = Github
                user = "jdx"
                repo = "mise"
                checksums = [] } ]

        printfn "number of repos: %d" (repos |> List.length)

        let! updatedRepos = repos |> List.map (fun r -> updateChecksumsNames r) |> Async.Parallel


        return!
            updatedRepos
            |> Array.map (fun r -> downloadLastChecksums r.user r.repo r.checksums)
            |> Async.Parallel


    }

main () |> Async.RunSynchronously
