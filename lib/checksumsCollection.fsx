namespace Asfaload.Collector

#load "Shared.fsx"
#r "nuget: Octokit, 13.0.1"
#r "nuget: FsHttp"
#r "nuget: Fsharp.Data"
#r "nuget: FsHttp.FSharpData, 14.5.1"
#r "nuget: FSharp.SystemTextJson, 1.3.13"
#r "nuget: Fli, 1.111.10"

module ChecksumsCollector =


    open Octokit
    open System
    open System.IO
    open FsHttp
    open FSharp.Data
    open FsHttp.FSharpData
    open FSharp.Data.JsonExtensions
    open Asfaload.Collector
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
          //DataDog/dd-trace-dotnet
          "sha512.txt"
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

    let gitPushIfAhead () =

        gitMutex.WaitOne() |> ignore

        let aheadCount =
            cli {
                Exec "git"
                Arguments [ "rev-list"; "--count"; "origin/master..master" ]
                WorkingDirectory(Environment.GetEnvironmentVariable "BASE_DIR")
            }
            |> Command.execute
            |> Output.throwIfErrored
            |> Output.toText
            |> int

        if aheadCount > 0 then
            cli {
                Exec "git"
                Arguments [ "push" ]
                WorkingDirectory(Environment.GetEnvironmentVariable "BASE_DIR")
            }
            |> Command.execute
            |> Output.throwIfErrored
            |> (fun o -> printfn "PUSH: %s" (o |> Output.toText))


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

    // Looks for most recent non-draft and non-prerelease
    let rec getLastGithubRelease (repo: Repo) =
        let rec looper (iteration: int) =
            async {
                let options = new ApiOptions(PageSize = 10, PageCount = iteration)

                let! releases =
                    client.Repository.Release.GetAll(repo.user, repo.repo, options)
                    |> Async.AwaitTask

                let proper = releases |> Seq.filter (fun r -> not r.Draft && not r.Prerelease)
                let count = (proper |> Seq.length)
                printfn "found %d releases " count

                if count = 0 then
                    return! looper (iteration + 1)
                else
                    return (proper |> Seq.head)

            }

        looper 1

    // FIXME: extract user and repo from url
    let getReleaseByUrl (user: string) (repo: string) (url: string) =
        async {
            // we look in 20 last releases
            let options = new ApiOptions(PageSize = 20, PageCount = 1)

            let! releases = client.Repository.Release.GetAll(user, repo, options) |> Async.AwaitTask

            let release = releases |> Seq.tryFind (fun r -> r.HtmlUrl = url)
            return release

        }


    let downloadLastChecksums (rel: Release) (r: Repo) =
        async {
            printfn "Running downloadLast for %s/%s" r.user r.repo
            let lastUri = System.Uri(rel.HtmlUrl)

            let downloadSegments =
                lastUri.Segments |> Array.map (fun s -> if s = "tag/" then "download/" else s)

            return!
                r.checksums
                |> List.map (fun name -> downloadIndividualChecksumsFile lastUri downloadSegments name)
                |> Async.Parallel

        }

    let updateChecksumsNames (rel: Release) (repo: Repo) =
        async {
            let releaseId = rel.Id

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

    let getReleaseChecksums (cleanup: unit -> unit) (release: Release) (repo: Repo) =
        async {
            let! updatedRepo = updateChecksumsNames release repo
            let! optionsArray = downloadLastChecksums release updatedRepo

            // If we downloaded a new checksums file, we need to commit
            if optionsArray |> Array.exists (fun o -> o.IsSome) then
                gitCommit $"{repo.kind.ToString()}://{repo.user}/{repo.repo}" |> ignore

            // Only cleanup if all went well
            cleanup ()
        }


    let handleRepoRelease (cleanup: unit -> unit) (repo: Repo) =
        async {
            let! release = getLastGithubRelease repo
            return! getReleaseChecksums cleanup release repo
        }
