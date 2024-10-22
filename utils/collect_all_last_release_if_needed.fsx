// Script to manually trigger a release collection
// dotnet fsi manually_send_release.fsx $user $repo
#load "../lib/Shared.fsx"
#load "../lib/Queue.fsx"

open System
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
    async {
        let args = fsi.CommandLineArgs

        if args |> Seq.length < 2 then
            printfn "requires as arguments: file_with_repos_urls"
            return 1
        else

            let file = args[1]

            let! _r =
                readLines file
                |> Seq.map (fun (l: string) -> l.Replace("https://github.com/", "").Split("/"))
                |> Seq.map (fun a -> (a[0], a[1]))
                // Dot not track all neovim forks
                |> Seq.filter (fun (_u, r) -> r <> "neovim")
                |> Seq.map (fun (user, repo) ->
                    { user = user
                      repo = repo
                      checksums = []
                      kind = Github })
                |> Seq.map (fun r ->
                    async {
                        if Directory.Exists(Path.Join([| baseDir; "github.com"; r.user; r.repo |])) then
                            printfn "local dir for %s/%s already exists, skipping trigger" r.user r.repo
                        else
                            do! Asfaload.Collector.Queue.triggerReleaseDownload r.user r.repo |> Async.AwaitTask
                            printfn "Requested download for %s/%s in queue" r.user r.repo
                            // Avoid secundary rate limits
                            do! Async.Sleep 300_000
                    })
                |> Async.Sequential

            return 0
    }

main () |> Async.RunSynchronously
