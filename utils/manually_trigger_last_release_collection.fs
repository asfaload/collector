// Utility to manually trigger a release collection
// manually_send_release.fsx $user $repo

open Asfaload.Collector


let args = System.Environment.GetCommandLineArgs()
let user = args[1]
let repoName = args[2]

printfn "triggering for %s/%s, Ok?" user repoName
printfn "press enter to continue, Ctrl-C to interrupt"
System.Console.ReadLine() |> ignore

let repo =
    { user = user
      repo = repoName
      kind = Github
      checksums = [] }

Queue.publishRepoRelease repo |> Async.AwaitTask |> Async.RunSynchronously
