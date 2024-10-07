// Script to collect checksums from new releases registered on the DiskQueue by the script
// looking for new notifications.
// It downloads the checksums under a directory hierarchy mimicking the source repo's path to the checksums file,
// including the host, under $BASE_DIR, which needs to be a git repo.
// It looks for checksums files in the release artifacts according to pre-defined patterns.
// When the checksums of a release have been downloaded, a new commit is registered.
#r "nuget: DiskQueue, 1.7.1"
#load "lib/Shared.fsx"
#r "nuget: Octokit, 13.0.1"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"
#r "nuget: FsHttp.FSharpData, 14.5.1"
#r "nuget: FSharp.SystemTextJson, 1.3.13"
#r "nuget: Fli, 1.111.10"

open Octokit
open System
open System.IO
open FsHttp
open FSharp.Data
open FsHttp.FSharpData
open FSharp.Data.JsonExtensions
open Asfaload.Collector
open DiskQueue
open System.Text.Json
open System.Text.RegularExpressions
open Fli



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
      "SHASUMS512"
      // Neovim's approach:
      ".*\.sha256sum" ]

let gitMutex = new System.Threading.Mutex()

let gitCommit (subject: string) =

    gitMutex.WaitOne() |> ignore

    cli {
        Exec "git"
        Arguments [ "commit"; "-m"; subject ]
        WorkingDirectory(Environment.GetEnvironmentVariable "BASE_DIR")
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

    gitMutex.ReleaseMutex()
    ()

let gitAdd (path: string) =
    gitMutex.WaitOne() |> ignore
    printfn "running git add %s" path

    cli {
        Exec "git"
        Arguments [ "add"; path ]
        WorkingDirectory(Environment.GetEnvironmentVariable "BASE_DIR")
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

    gitMutex.ReleaseMutex()
    path

// Returns Some only if the directory was created.
// If the directory existed or in case of error, returns None
let createReleaseDir (path: string) =
    printfn "createReleaseDir got path %s" path
    let baseDir = Environment.GetEnvironmentVariable("BASE_DIR")
    printfn "basedir = %s" baseDir
    // Second path needs to be relative, or it iss returned as result.....
    let absoluteDirPath = Path.Combine(baseDir, path)
    printfn "absolute dir path = %s" absoluteDirPath

    if Directory.Exists absoluteDirPath then
        // Path exists, return Some to continue processing
        Some absoluteDirPath
    else
        let dir = Directory.CreateDirectory absoluteDirPath
        // Return None when directory cannot be created, to stop further processing
        if dir.Exists then Some absoluteDirPath else None

// Returns None if no download took place
let downloadChecksums (checksumsUri: Uri) destinationDir =
    let filename = checksumsUri.Segments |> Array.last
    let filePath = Path.Combine(destinationDir, filename)
    printfn "downloading %s" (checksumsUri.ToString())

    // We do not re-download existing files
    if not (File.Exists filePath) then
        get (checksumsUri.ToString()) |> Request.send |> Response.saveFile filePath
        Some filePath
    else
        // If file exists, return None to stop further processing
        None

let tokenAuth =
    new Octokit.Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))

let client = new GitHubClient(new ProductHeaderValue("my-testing-app"))
client.Credentials <- tokenAuth


let downloadIndividualChecksumsFile (lastUri: Uri) (downloadSegments: string array) (filename: string) =
    async {
        let checksumsSegments = Array.append downloadSegments [| filename |]
        let checksumsPath = Path.Join(checksumsSegments)
        let checksumsUri = System.Uri(lastUri, checksumsPath)

        let resultingOption =
            downloadSegments
            // Remove leading "/" segment, as it caused trouble when calling Path.GetRelativePath
            |> Array.filter (fun s -> s <> "/")
            // Set the hostname as first segment
            |> Array.append [| lastUri.Host |]
            |> Path.Combine
            |> createReleaseDir
            |> Option.bind (downloadChecksums checksumsUri)
            |> Option.map gitAdd


        match resultingOption with
        | Some path -> printfn "New checksums file downloaded at %s" path
        | None -> printfn "No download took place"

        return resultingOption

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
            |> Array.filter (fun a ->
                CHECKSUMS
                |> List.exists (fun chk ->
                    let regex = Regex(chk)
                    regex.IsMatch(a?name.AsString())))
            |> Array.map (fun a -> a?name.AsString())
            |> Array.toList

        printfn "found checksums files %A" checksumsFiles

        return { repo with checksums = checksumsFiles }
    }


let handleRepoRelease (qSession: IPersistentQueueSession) (repo: Repo) =
    async {
        let! updatedRepos = [ repo ] |> List.map (fun r -> updateChecksumsNames r) |> Async.Parallel

        let! options =
            updatedRepos
            |> Array.map (fun r -> downloadLastChecksums r.user r.repo r.checksums)
            |> Async.Parallel

        let optionsArray = options |> Array.reduce Array.append

        // If we downloaded a new checksums file, we need to commit
        if optionsArray |> Array.exists (fun o -> o.IsSome) then
            gitCommit $"{repo.kind.ToString()}://{repo.user}/{repo.repo}" |> ignore

        // Only flush if all went well
        qSession.Flush()

    }

let rec readQueue (queue: string) =
    async {
        // This will wait until the queue can be locked.
        let releasingReposQueue = PersistentQueue.WaitFor(queue, TimeSpan.FromSeconds(3))

        let qSession = releasingReposQueue.OpenSession()

        let repo =
            qSession.Dequeue()
            |> Option.ofObj
            |> Option.map System.Text.Encoding.ASCII.GetString
            |> Option.map JsonSerializer.Deserialize<Repo>

        match repo with
        | None ->
            qSession.Dispose()
            // Release the queue so writer can access it
            releasingReposQueue.Dispose()
            printfn "Nothing in queue, will sleep 1s"
            do! Async.Sleep 1000
            return! readQueue queue
        | Some repo ->
            printfn "repo = %A" repo
            do! handleRepoRelease qSession repo
            qSession.Dispose()
            // Release the queue so writer can access it
            releasingReposQueue.Dispose()
            return! readQueue queue

    }

let main () =
    async {
        let! _ = readQueue (Environment.GetEnvironmentVariable("RELEASES_QUEUE"))
        return 0
    }

main () |> Async.RunSynchronously
