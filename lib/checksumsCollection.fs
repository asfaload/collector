namespace Asfaload.Collector


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



    let CHECKSUMS = ChecksumHelpers.CHECKSUMS
    let baseDir = Environment.GetEnvironmentVariable "BASE_DIR"

    let gitMutex = new System.Threading.Mutex()

    let gitCommit (subject: string) =

        gitMutex.WaitOne() |> ignore

        cli {
            Exec "git"
            Arguments [ "commit"; "-m"; subject ]
            WorkingDirectory(baseDir)
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
                WorkingDirectory(baseDir)
            }
            |> Command.execute
            |> Output.throwIfErrored
            |> Output.toText
            |> int

        if aheadCount > 0 then
            cli {
                Exec "git"
                Arguments [ "push" ]
                WorkingDirectory(baseDir)
            }
            |> Command.execute
            |> Output.throwIfErrored
            |> (fun o -> printfn "PUSH: %s" (o |> Output.toText))


        gitMutex.ReleaseMutex()
        ()

    let gitAdd (baseDir: string) (path: string) =
        gitMutex.WaitOne() |> ignore
        printfn "running git add %s" path

        cli {
            Exec "git"
            Arguments [ "add"; path ]
            WorkingDirectory(baseDir)
        }
        |> Command.execute
        |> Output.throwIfErrored
        |> ignore

        gitMutex.ReleaseMutex()
        path

    // Returns Some only if the directory was created.
    // If the directory existed or in case of error, returns None
    let createReleaseDir (baseDir: string) (path: string) =
        printfn "createReleaseDir got path %s" path

        printfn "basedir = %s" baseDir
        // Second path needs to be relative, or it iss returned as result.....
        let absoluteDirPath = Path.Combine(baseDir, path)
        printfn "absolute dir path = %s" absoluteDirPath

        if Directory.Exists absoluteDirPath then
            // Path exists, return Some to continue processing
            Some absoluteDirPath
        else
            try
                let dir = Directory.CreateDirectory absoluteDirPath
                // Return None when directory cannot be created, to stop further processing
                if dir.Exists then Some absoluteDirPath else None
            with e ->
                None

    // Returns None if no download took place
    let downloadChecksums (checksumsUri: Uri) destinationDir =
        let filename = checksumsUri.Segments |> Array.last
        let filePath = Path.Combine(destinationDir, filename)
        printfn "downloading %s" (checksumsUri.ToString())

        // We do not re-download existing files
        if not (File.Exists filePath) then
            let response = get (checksumsUri.ToString()) |> Request.send

            // Only save file if request was successful
            // FIXME: we should also look at other successful status codes
            if response.statusCode = Net.HttpStatusCode.OK then
                response |> Response.saveFile filePath
                printfn "Saved checksums file"
                Some filePath
            else
                printfn
                    "ERROR: download gave unexpected status code %A for url %s"
                    response.statusCode
                    (checksumsUri.ToString())

                None
        else
            // If file exists, return None to stop further processing
            None

    let tokenAuth =
        new Octokit.Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))

    let client = new GitHubClient(new ProductHeaderValue("asfaload-collector"))
    client.Credentials <- tokenAuth


    let getDownloadDir (host: string) (downloadSegments: string array) =
        downloadSegments
        // Remove leading "/" segment, as it caused trouble when calling Path.GetRelativePath
        |> Array.filter (fun s -> s <> "/")
        // Set the hostname as first segment
        |> Array.append [| host |]
        |> Path.Combine

    let downloadIndividualChecksumsFile
        (baseDir: string)
        (lastUri: Uri)
        (downloadSegments: string array)
        (filename: string)
        =
        async {
            let checksumsSegments = Array.append downloadSegments [| filename |]
            let checksumsPath = Path.Join(checksumsSegments)
            let checksumsUri = System.Uri(lastUri, checksumsPath)

            let resultingOption =
                downloadSegments
                |> getDownloadDir lastUri.Host
                |> createReleaseDir baseDir
                |> Option.bind (downloadChecksums checksumsUri)
                |> Option.map (gitAdd baseDir)


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
                    if iteration < 10 then
                        // Avoid secundary rate limits
                        do! Async.Sleep 1000
                        return! looper (iteration + 1)
                    else
                        printfn "No release found for %s/%s" repo.user repo.repo
                        return None
                else
                    return (proper |> Seq.head |> Some)

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


    let downloadReleaseChecksums
        (baseDir: string)
        (releaseHtmlUrl: string)
        (releasePublishedAt: Nullable<DateTimeOffset>)
        (r: Repo)
        =
        let toOption (nullable: Nullable<_>) =
            if nullable.HasValue then Some nullable.Value else None

        async {
            printfn "Running downloadLast for %s/%s" r.user r.repo


            let lastUri = System.Uri(releaseHtmlUrl)

            let downloadSegments =
                lastUri.Segments |> Array.map (fun s -> if s = "tag/" then "download/" else s)

            let checksumsAsyncs =
                r.checksums
                |> List.map (fun name ->
                    async {
                        do! Async.Sleep 1000
                        return! downloadIndividualChecksumsFile baseDir lastUri downloadSegments name
                    })

            let relativeDownloadDir = getDownloadDir lastUri.Host downloadSegments

            let downloadDir = Path.Combine(baseDir, relativeDownloadDir)


            let generateIndexAsync =
                async {
                    if Directory.Exists downloadDir then
                        printfn "will generate index for downloadDir %s" downloadDir

                        Index.generateChecksumsList
                            downloadDir
                            (toOption releasePublishedAt)
                            (Some DateTimeOffset.UtcNow)

                        gitAdd baseDir downloadDir |> ignore
                    else
                        printfn "not generating index for inexisting directory %s" downloadDir

                    // As we run this async at the same level as the asyncs downloading checksums
                    // files, we return None here to be sure it is not handled as a checksums file
                    return None
                }

            return! List.append checksumsAsyncs [ generateIndexAsync ] |> Async.Sequential

        }

    let updateChecksumsNames (release: Release) (repo: Repo) =
        async {

            if (isNull (Environment.GetEnvironmentVariable("DEBUG"))) then
                do! Async.Sleep 60_000



            let checksumsFiles =
                release.Assets |> Seq.map (fun a -> a.Name) |> ChecksumHelpers.filterChecksums

            printfn "found checksums files %A" checksumsFiles
            return { repo with checksums = checksumsFiles }

        }

    let updateChecksumsNamesFromReleaseId user (repo: Repo) releaseId =
        async {

            if (isNull (Environment.GetEnvironmentVariable("DEBUG"))) then
                do! Async.Sleep 60_000

            let! response =
                http {
                    GET $"https://api.github.com/repos/{user}/{repo}/releases/{releaseId}/assets"
                    Accept "application/vnd.github+json"
                    UserAgent "asfaload-collector"
                    //AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
                    header "X-GitHub-Api-Version" "2022-11-28"
                //header "If-Modified-Since" "Mon, 30 Sep 2024 09:21:13 GMT"
                }
                |> Request.sendAsync
            // Ensure we avoid secundary rate limits
            do! Async.Sleep 1000

            match response.statusCode with
            | Net.HttpStatusCode.OK ->
                let json = response |> Response.toJson

                let checksumsFiles =
                    json.AsArray()
                    |> Array.map (fun e -> e?name.ToString())
                    |> ChecksumHelpers.filterChecksums

                printfn "found checksums files %A" checksumsFiles
                return { repo with checksums = checksumsFiles }
            | _ ->
                printfn
                    "%s we got an error for %s/%s: %s!\n%s\nWe sleep one hour.\nHeader retry-after= %s "
                    (DateTime.Now.ToString())
                    (repo.user)
                    (repo.repo)
                    (response.reasonPhrase)
                    (response.statusCode.ToString())
                    (response.headers.TryGetValues "retry-after"
                     |> (fun (present, values) -> if present then (Some(values |> Seq.head)) else None)
                     |> Option.defaultValue "Absent")

                do! Async.Sleep 3600_000
                return repo

        }

    let validateRelease (repo: Repo) (releaseInfo: ReleaseInfo) =
        async {
            match releaseInfo with
            | OctokitRelease release -> return release
            | CallbackRelease rel ->
                return! client.Repository.Release.Get(repo.user, repo.repo, rel.Id) |> Async.AwaitTask
        }

    let getReleaseChecksums (release: ReleaseInfo) (repo: Repo) =
        async {

            let! validatedRelease = validateRelease repo release
            let! updatedRepo = updateChecksumsNames validatedRelease repo

            let! optionsArray =
                downloadReleaseChecksums baseDir validatedRelease.HtmlUrl validatedRelease.PublishedAt updatedRepo

            // If we downloaded a new checksums file, we need to commit
            if optionsArray |> Array.exists (fun o -> o.IsSome) then
                gitCommit $"{repo.kind.ToString()}://{repo.user}/{repo.repo}" |> ignore

        }

    let getReleaseChecksumsFromId user repoName (releaseId: int64) =
        task {
            let repo =
                { kind = Github
                  user = user
                  repo = repoName
                  checksums = [] }

            let! release = client.Repository.Release.Get(user, repoName, releaseId)
            do! getReleaseChecksums (OctokitRelease release) repo
            ()
        }


    let handleRepoRelease (repo: Repo) =
        async {
            let! release = getLastGithubRelease repo

            match release with
            | None -> ()
            | Some release -> return! getReleaseChecksums (OctokitRelease release) repo
        }

    let handleCallbackRelease (body: ReleaseCallbackBody.Root) =
        let user, hasReleaseInBody =
            try
                // try to get user from release in body
                let user = body.Release.Author.Login
                user, true
            with _e ->
                // Here we got no release in the body, eg due to a release-please triggered call
                body.Repository.Owner.Login, false

        let repo =
            { kind = Github
              user = user
              repo = body.Repository.Name
              checksums = [] }

        if hasReleaseInBody then
            getReleaseChecksums (CallbackRelease body.Release) repo
        else
            // FIXME: this is not foolproof in case of 2 rapid subsequent releases. We will miss the penultimate one if the second
            // release is done before we run this.
            printfn "NOTICE: release callback doesn't have release in body. Will retrieve last release from github."

            async {
                let! releaseOption = getLastGithubRelease repo

                match releaseOption with
                | Some release -> return! getReleaseChecksums (OctokitRelease release) repo
                | None ->
                    printfn "ERROR: !!! No last release found although got release callback"
                    return ()
            }
