#r "nuget: Octokit, 13.0.1"
#r "nuget: FsHttp"

open Octokit
open System
open System.IO
open FsHttp

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

let downloadLastChecksums (username: string) (repo: string) (checksums: string list) =
    async {
        printfn "Running downloadLast for %s/%s" username repo
        let options = new ApiOptions(PageSize = 3, PageCount = 1)
        let! releases = client.Repository.Release.GetAll(username, repo, options) |> Async.AwaitTask
        let last = releases |> Seq.head
        let lastUri = System.Uri(last.HtmlUrl)

        let downloadSegments =
            lastUri.Segments |> Array.map (fun s -> if s = "tag/" then "download/" else s)

        return!
            checksums
            |> List.map (fun name -> downloadIndividualChecksumsFile lastUri downloadSegments name)
            |> Async.Parallel

    }

type RepoKind =
    | Github
    | Gitlab

type Repo =
    { kind: RepoKind
      user: string
      repo: string
      checksums: string list }

let main () =
    async {
        let repos =
            [ { kind = Github
                user = "jesseduffield"
                repo = "lazygit"
                checksums = [ "checksums.txt" ] }
              { kind = Github
                user = "jdx"
                repo = "mise"
                checksums = [ "SHASUMS256.txt"; "SHASUMS512.txt" ] } ]

        return!
            repos
            |> List.map (fun r -> downloadLastChecksums r.user r.repo r.checksums)
            |> Async.Parallel


    }

main () |> Async.RunSynchronously
