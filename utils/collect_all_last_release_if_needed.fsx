// Script to manually trigger a release collection
// dotnet fsi manually_send_release.fsx $user $repo
#r "nuget: DiskQueue, 1.7.1"
#load "../lib/Shared.fsx"
#load "../lib/Queue.fsx"

open System
open DiskQueue
open Asfaload.Collector
open System.Text.Json
open System.IO

let baseDir = Environment.GetEnvironmentVariable("BASE_DIR")

//let queue = Environment.GetEnvironmentVariable("RELEASES_QUEUE")
//let releasingReposQueue = PersistentQueue.WaitFor(queue, TimeSpan.FromSeconds(300))
//let qSession = releasingReposQueue.OpenSession()


let readLines (filePath: string) =
    seq {
        use sr = new StreamReader(filePath)

        while not sr.EndOfStream do
            yield sr.ReadLine()
    }



let main () =
    let args = fsi.CommandLineArgs

    if args |> Seq.length < 2 then
        printfn "requires as arguments: file_with_repos_urls"
        1
    else

        let file = args[1]

        readLines file
        |> Seq.map (fun (l: string) -> l.Replace("https://github.com/", "").Split("/"))
        |> Seq.map (fun a -> (a[0], a[1]))
        |> Seq.map (fun (user, repo) ->
            { user = user
              repo = repo
              checksums = []
              kind = Github })
        |> Seq.iter (fun r ->
            try
                if Directory.Exists(Path.Join([| baseDir; "github.com"; r.user; r.repo |])) then
                    printfn "local dir for %s/%s already exists, skipping trigger" r.user r.repo
                else
                    Asfaload.Collector.Queue.triggerReleaseDownload r.user r.repo
                    printfn "triggered download for %s/%s" r.user r.repo
            with e ->
                printfn "Exception triggering download for repo: %s" e.Message)

        0

main ()
