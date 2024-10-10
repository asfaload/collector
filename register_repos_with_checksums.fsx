// Script to manually download the checksums of a specific release
// dotnet fsi manually_get_release_checksums.fsx $user $repo $release_url
#r "nuget: DiskQueue, 1.7.1"
#r "nuget: System.Data.SQLite, 1.0.119"
#load "lib/db.fsx"
#load "lib/Shared.fsx"
#load "lib/checksumsCollection.fsx"
#load "lib/Queue.fsx"

open Asfaload.Collector
open Asfaload.Collector.DB
open Asfaload.Collector.ChecksumsCollector
open System.IO

let args = fsi.CommandLineArgs

let readLines (filePath: string) =
    seq {
        use sr = new StreamReader(filePath)

        while not sr.EndOfStream do
            yield sr.ReadLine()
    }

let loop () =
    async {

        if args |> Seq.length < 2 then
            printfn "requires as arguments: file_with_repos_urls"
            return 1
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
                    let _created = Repos.create r.user r.repo |> Repos.run |> Async.RunSynchronously
                    Asfaload.Collector.Queue.triggerReleaseDownload r.user r.repo
                    printfn "inserted %s/%s" r.user r.repo
                with e ->
                    printfn "Exception inserting new repo: %s" e.Message)

            return 0
    }

loop () |> Async.RunSynchronously
