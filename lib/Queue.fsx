#r "nuget: DiskQueue, 1.7.1"
#load "Shared.fsx"
namespace Asfaload.Collector

module Queue =
    open System
    open DiskQueue
    open System.Text.Json

    let triggerReleaseDownload (user: string) (repo: string) =
        let queue = Environment.GetEnvironmentVariable("RELEASES_QUEUE")
        let releasingReposQueue = PersistentQueue.WaitFor(queue, TimeSpan.FromSeconds(300))
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
