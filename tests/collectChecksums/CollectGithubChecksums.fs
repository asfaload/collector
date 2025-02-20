module tests.CollectGithubChecksums

open FsHttp
open FsHttp.Tests.TestHelper
open FsHttp.Tests.Server

open Asfaload.Collector
open Asfaload.Collector.ChecksumsCollector

open NUnit.Framework
open NUnit
open FsUnit

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful

open System
open System.IO

open Fli

[<SetUp>]
let Setup () = ()

[<OneTimeSetUp>]
let OneTimeSetup () =
    System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener())
    |> ignore

[<OneTimeTearDown>]
let OneTimeTearDown () = System.Diagnostics.Trace.Flush()

let gitInitFull (bare: bool) (dir: string) =
    cli {
        Exec "git"
        Arguments(if bare then [ "init"; "--bare" ] else [ "init" ])
        WorkingDirectory(dir)
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore
// Used to initial a git repo for tests
let gitInit = gitInitFull false
let gitInitBare = gitInitFull true

let gitLog (dir: string) =
    let r =
        cli {
            Exec "git"
            Arguments [ "log"; "--pretty=format:%s" ]
            WorkingDirectory(dir)
        }
        |> Command.execute
        |> Output.throwIfErrored

    r.Text

let gitClone (remote: string) (local: string) =
    let r =
        cli {
            Exec "git"
            Arguments [ "clone"; remote; local ]
        }
        |> Command.execute
        |> Output.throwIfErrored

    r |> Output.toExitCode

let gitDiffCached (dir: string) =
    let r =
        cli {
            Exec "git"
            Arguments [ "diff"; "--cached"; "--name-only" ]
            WorkingDirectory(dir)
        }
        |> Command.execute
        |> Output.throwIfErrored

    r.Text

let initialiseRemoteAndLocalClone () =
    // Create new bare repo as remote
    let remoteDir = Directory.CreateTempSubdirectory().FullName
    gitInitBare remoteDir

    // Create new local clone
    let baseDir = Directory.CreateTempSubdirectory().FullName
    gitClone remoteDir baseDir |> should equal 0
    // Add first file to the repo
    let firstFile = Path.Combine(baseDir, "first_file.txt")
    File.WriteAllText(firstFile, "This file was present on remote")
    gitAdd baseDir firstFile |> should equal firstFile
    gitCommitInDir baseDir "\"first commit in remote\""
    // Check the first commit is added as expected
    gitLog baseDir |> should equal (Some "first commit in remote")
    //
    // Push to the remote so it has a commit. Otherwise, the command
    // checking if the local is ahead fails (git rev-list --count origin/master..master)
    cli {
        Exec "git"
        Arguments [ "push" ]
        WorkingDirectory(baseDir)
    }
    |> Command.execute
    |> Output.throwIfErrored
    |> ignore

    // Check the first commit was pushed to the remote as expected
    gitLog remoteDir |> should equal (Some "first commit in remote")
    remoteDir, baseDir

[<Test>]
let test_createReleaseDir () =
    let tempDir = Directory.CreateTempSubdirectory().FullName


    // Create a new release dir
    let subDir = "firstDir"
    let expectedDir = Path.Combine(tempDir, subDir)
    let r = createReleaseDir tempDir subDir
    r |> should equal (Some expectedDir)
    Directory.Exists expectedDir |> should equal true

    // Request creation of already existing dir
    let r = createReleaseDir tempDir subDir
    r |> should equal (Some expectedDir)
    Directory.Exists expectedDir |> should equal true

    // Error creating directory
    let expectedDir = "/bin/to/be/deleted"
    let r = createReleaseDir "/bin" "to/be/deleted"
    r |> should equal None
    Directory.Exists expectedDir |> should equal false

[<Test>]
let test_downloadChecksums () =

    use _server =
        GET
        >=> choose
                [ path "/get_checksums" >=> OK "checksums file content"
                  path "/error" >=> Suave.RequestErrors.NOT_FOUND "File not found" ]
        |> serve

    let checksumsUri = System.Uri(url "/get_checksums")

    // Download new file
    let tempDir = Directory.CreateTempSubdirectory().FullName
    let expectedPath = Path.Combine(tempDir, "get_checksums")
    File.Exists(expectedPath) |> should equal false
    let r = downloadChecksums checksumsUri tempDir
    printfn "tempDir = %s" tempDir
    r |> should equal (Some expectedPath)
    File.Exists(expectedPath) |> should equal true

    // Download existing file
    File.Exists(expectedPath) |> should equal true
    let r = downloadChecksums checksumsUri tempDir
    r |> should equal None
    File.Exists(expectedPath) |> should equal true

    // Download error
    let errorURI = System.Uri(url "/error")
    let r = downloadChecksums errorURI tempDir
    r |> should equal None


[<Test>]
let test_getDownloadDir () =
    let downloadUrl =
        "https://github.com/asfaload/asfald/releases/download/v0.5.1/asfald-aarch64-unknown-linux-musl"

    let downloadUri = System.Uri(downloadUrl)
    let r = getDownloadDir "github.com" (downloadUri.Segments)

    r
    |> should equal "github.com/asfaload/asfald/releases/download/v0.5.1/asfald-aarch64-unknown-linux-musl"

[<Test>]
let test_downloadIndividualChecksumsFile () =
    async {

        let releasePath = "asfaload/asfald/releases/download/v0.5.1"

        use _server =
            GET
            >=> choose
                    [ path $"/{releasePath}/checksums.txt" >=> OK "checksums file content"
                      path $"/{releasePath}/checksums_512.txt"
                      >=> Suave.RequestErrors.NOT_FOUND "File not found" ]
            |> serve

        // Create new repo
        let baseDir = Directory.CreateTempSubdirectory().FullName
        gitInit baseDir

        // Setup variables
        let fullUri = Uri($"http://127.0.0.1:8080/{releasePath}")
        let host = fullUri.Host
        let segments = fullUri.Segments
        let fileName = "checksums.txt"

        // Issue call to tested function
        let! r = downloadIndividualChecksumsFile baseDir fullUri segments fileName

        // Check the file has been downloaded
        r |> should equal (Some $"{baseDir}/{host}/{releasePath}/checksums.txt")

        // Check it was git added, but not committed
        gitDiffCached baseDir
        |> should equal (Some $"{host}/{releasePath}/checksums.txt")

        // Error in download of file
        // -------------------------

        // Create new repo
        let baseDir = Directory.CreateTempSubdirectory().FullName
        gitInit baseDir

        // Setup variables
        let fullUri = Uri($"http://127.0.0.1:8080/{releasePath}")
        let host = fullUri.Host
        let segments = fullUri.Segments
        let fileName = "checksums_512.txt"

        // Issue call to tested function
        let! r = downloadIndividualChecksumsFile baseDir fullUri segments fileName

        // Check we don't fail but return None for failed download
        r |> should equal None

        // Check nothing was added to git
        gitDiffCached baseDir |> should equal None

        ()

    }

[<Test>]
let test_downloadReleaseChecksums () =
    async {

        let releasePath = "asfaload/asfald/releases/download/v0.5.1"

        use _server =
            GET
            >=> choose
                    [ path $"/{releasePath}/checksums.txt"
                      >=> OK(File.ReadAllText "fixtures/checksums.txt")
                      path $"/{releasePath}/checksums_512.txt"
                      >=> OK(File.ReadAllText "fixtures/checksums.txt")
                      path $"/{releasePath}/checksums_1024.txt"
                      >=> Suave.RequestErrors.NOT_FOUND "File not found" ]
            |> serve

        // Create new repo
        let baseDir = Directory.CreateTempSubdirectory().FullName
        gitInit baseDir

        // Setup variables
        let fullUri = Uri($"http://127.0.0.1:8080/{releasePath}")
        let host = fullUri.Host
        let segments = fullUri.Segments
        let fileName = "checksums.txt"

        let repo: Repo =
            { kind = Github
              user = "asfaload"
              repo = "asfald"
              checksums = [ "checksums.txt"; "checksums_512.txt" ] }

        let publishedAt = Nullable<DateTimeOffset>(DateTimeOffset.Now.AddDays(-1))
        let! r = downloadReleaseChecksums baseDir (fullUri.ToString()) publishedAt repo

        r
        |> should
            equal
            [| (Some $"{baseDir}/{host}/{releasePath}/checksums.txt")
               (Some $"{baseDir}/{host}/{releasePath}/checksums_512.txt")
               // The None is the return of the async generating the index file
               None |]

        File.Exists($"{baseDir}/{host}/{releasePath}/asfaload.index.json")
        |> should equal true

        ()
    }

[<Test>]
let test_updateChecksumsNames () =
    task {
        let repo =
            { kind = Github
              user = "asfaload"
              repo = "asfald"
              checksums = [] }

        let assetNames =
            [| "checksums.txt"
               "checksums_512.txt"
               "my_soft.tgz.sha256"
               "my_soft.tgz"
               "lib.tgz" |]

        let! r = updateChecksumsNames assetNames repo

        r
        |> should
            equal
            { repo with
                checksums = [ "checksums.txt"; "my_soft.tgz.sha256" ] }

    }

[<Test>]
let testGitPush () =
    async {
        let remoteDir, baseDir = initialiseRemoteAndLocalClone ()
        // Add another file
        let addedFilePath = Path.Combine(baseDir, "addedFile")
        File.WriteAllText(addedFilePath, "This file was added to the git repo after the clone")
        gitAdd baseDir addedFilePath |> should equal addedFilePath
        gitCommitInDir baseDir "\"File added by test testGitPush\""
        // Check the commit is added locally
        gitLog baseDir
        |> should equal (Some "File added by test testGitPush\nfirst commit in remote")

        // push to remote
        gitPushIfAheadInDir baseDir (TimeSpan.FromMicroseconds 1)

        // Check commit was pushed to the remote
        gitLog remoteDir
        |> should equal (Some "File added by test testGitPush\nfirst commit in remote")
    }

[<Test>]
let testGitCoallescedPush () =
    async {
        let remoteDir, baseDir = initialiseRemoteAndLocalClone ()
        // Add another file
        let addedFilePath = Path.Combine(baseDir, "addedFile")
        File.WriteAllText(addedFilePath, "This file was added to the git repo after the clone")
        gitAdd baseDir addedFilePath |> should equal addedFilePath
        gitCommitInDir baseDir "\"File added by test testGitPush\""
        // Check the commit is added locally
        gitLog baseDir
        |> should equal (Some "File added by test testGitPush\nfirst commit in remote")

        // push to remote
        gitPushIfAheadInDir baseDir (TimeSpan.FromMinutes 5)
        // Check commit was pushed to the remote
        gitLog remoteDir
        |> should equal (Some "File added by test testGitPush\nfirst commit in remote")


        // Add another file
        let addedFilePath = Path.Combine(baseDir, "secondAddedFile")
        File.WriteAllText(addedFilePath, "This file was added as second to the repo")
        gitAdd baseDir addedFilePath |> should equal addedFilePath
        gitCommitInDir baseDir "\"Second file added by test testGitPush\""

        gitLog baseDir
        |> should
            equal
            (Some "Second file added by test testGitPush\nFile added by test testGitPush\nfirst commit in remote")
        // push to remote, but to rapidly after previous push
        gitPushIfAheadInDir baseDir (TimeSpan.FromMinutes 5)
        // Check commit was NOT pushed
        gitLog remoteDir
        |> should equal (Some "File added by test testGitPush\nfirst commit in remote")
        // Now push with a lower coallescing timespan
        do! Async.Sleep(TimeSpan.FromMilliseconds 1)
        gitPushIfAheadInDir baseDir (TimeSpan.FromMicroseconds 1)

        gitLog remoteDir
        |> should
            equal
            (Some "Second file added by test testGitPush\nFile added by test testGitPush\nfirst commit in remote")
    }
