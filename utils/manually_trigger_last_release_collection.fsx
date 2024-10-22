// Script to manually trigger a release collection
// dotnet fsi manually_send_release.fsx $user $repo
#load "../lib/Shared.fsx"
#load "../lib/Queue.fsx"

open Asfaload.Collector


let args = fsi.CommandLineArgs
let user = args[1]
let repoName = args[2]

let repo =
    { user = user
      repo = repoName
      kind = Github
      checksums = [] }

Queue.publish repo |> Async.AwaitTask |> Async.RunSynchronously
