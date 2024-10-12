// Script to manually trigger a release collection
// dotnet fsi manually_send_release.fsx $user $repo
#r "nuget: DiskQueue, 1.7.1"
#load "../lib/Shared.fsx"

open System
open DiskQueue
open Asfaload.Collector
open System.Text.Json

let queue = Environment.GetEnvironmentVariable("RELEASES_QUEUE")
let releasingReposQueue = PersistentQueue.WaitFor(queue, TimeSpan.FromSeconds(600))
let qSession = releasingReposQueue.OpenSession()

let args = fsi.CommandLineArgs
let user = args[1]
let repoName = args[2]

let repo =
    { user = user
      repo = repoName
      kind = Github
      checksums = [] }

qSession.Enqueue(repo |> JsonSerializer.Serialize |> System.Text.Encoding.ASCII.GetBytes)
qSession.Flush()
qSession.Dispose()
releasingReposQueue.Dispose()
