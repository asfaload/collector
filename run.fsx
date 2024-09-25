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
    printfn "absolute file path = %s" absoluteDirPath

    if Directory.Exists absoluteDirPath then
        printfn "Directory exists, returning None to stop further processing"
        None
    else
        let dir = Directory.CreateDirectory absoluteDirPath
        if dir.Exists then Some absoluteDirPath else None

let downloadChecksums (checksumsUri: Uri) destinationDir =
    let filename = checksumsUri.Segments |> Array.last
    let filePath = Path.Combine(destinationDir, filename)
    get (checksumsUri.ToString()) |> Request.send |> Response.saveFile filePath
    filePath

let tokenAuth = new Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
let client = new GitHubClient(new ProductHeaderValue("my-testing-app"))
client.Credentials <- tokenAuth


let downloadLast (username: string) (repo: string) =
    async {
        printfn "Running downloadLast for %s/%s" username repo
        let! releases = client.Repository.Release.GetAll(username, repo) |> Async.AwaitTask
        let last = releases |> Seq.head
        let lastUri = System.Uri(last.HtmlUrl)
        let lastPath = lastUri.AbsolutePath

        let downloadSegments =
            lastUri.Segments |> Array.map (fun s -> if s = "tag/" then "download/" else s)

        let checksumsSegments = Array.append downloadSegments [| "checksums.txt" |]
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

type RepoKind =
    | Github
    | Gitlab

type Repo =
    { kind: RepoKind
      user: string
      repo: string
      checksum: string }

let main () =
    async {
        let repos =
            [ { kind = Github
                user = "jesseduffield"
                repo = "lazygit"
                checksum = "checksums.txt" }
              { kind = Github
                user = "jdx"
                repo = "mise"
                checksum = "SHASUMS256.txt" } ]

        return! repos |> List.map (fun r -> downloadLast r.user r.repo) |> Async.Parallel


    }

main () |> Async.RunSynchronously
