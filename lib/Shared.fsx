#r "nuget: FSharp.SystemTextJson, 1.3.13"
#r "nuget: DiskQueue, 1.7.1"

namespace Asfaload.Collector

open System.Text.Json.Serialization

[<JsonFSharpConverter>]
type RepoKind =
    | Github
    | Gitlab

[<JsonFSharpConverter>]
type Repo =
    { kind: RepoKind
      user: string
      repo: string
      checksums: string list }

module Queue =
    open System
    open DiskQueue
    open System.Text.Json

    let triggerReleaseDownload (user: string) (repo: string) =
        let queue = Environment.GetEnvironmentVariable("RELEASES_QUEUE")
        let releasingReposQueue = PersistentQueue.WaitFor(queue, TimeSpan.FromSeconds(3))
        let qSession = releasingReposQueue.OpenSession()

        let repo =
            { user = user
              repo = repo
              kind = Github
              checksums = [] }

        qSession.Enqueue(repo |> JsonSerializer.Serialize |> System.Text.Encoding.ASCII.GetBytes)
        qSession.Flush()
        qSession.Dispose()
        releasingReposQueue.Dispose()
