// Script to manually download the checksums of a specific release
// dotnet fsi manually_get_release_checksums.fsx $user $repo $release_url
#load "../lib/Shared.fsx"
#load "../lib/checksumsCollection.fsx"

open Asfaload.Collector
open Asfaload.Collector.ChecksumsCollector

let args = fsi.CommandLineArgs


let main () =
    async {

        if args |> Seq.length < 4 then
            printfn "requires as arguments: user, repo and release url"
            return 1
        else

            let user = args[1]
            let repo = args[2]
            let url = args[3]

            let (repo: Repo) =
                { user = user
                  repo = repo
                  checksums = []
                  kind = Github }



            let! release = getReleaseByUrl repo.user repo.repo url

            match release with
            | None -> printfn "RELEASE NOT FOUND!"
            | Some release ->
                printfn "found release"
                do! getReleaseChecksums release repo

            return 0
    }

main () |> Async.RunSynchronously
