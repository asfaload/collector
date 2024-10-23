#r "nuget: NATS.Net, 2.5.2"
#r "nuget: FSharp.Control.TaskSeq"
namespace Asfaload.Collector

module Queue =
    open System
    open System.Text.Json


    open NATS.Client.JetStream
    open NATS.Client.JetStream.Models
    open NATS.Net
    open FSharp.Control


    let getContext () =
        let url = Environment.GetEnvironmentVariable("NATS_URL")
        let nc = new NatsClient(url)
        nc.CreateJetStreamContext()

    let getStream () =
        task {

            let js = getContext ()
            let streamName = "RELEASES"

            return!
                js.CreateStreamAsync(
                    new StreamConfig(
                        streamName,
                        subjects = [| "releases.>" |],
                        Retention = StreamConfigRetention.Workqueue
                    )
                )

        }

    let publish (repo: Repo) =
        task {
            // Need to initialise stream here
            let! stream = getStream ()
            let js = getContext ()
            let! ack = js.PublishAsync($"releases.new.{repo.user}/{repo.repo}", repo |> JsonSerializer.Serialize)
            ack.EnsureSuccess()
        }

    let consumeReleases (f: Repo -> unit) =
        task {
            let! stream = getStream ()
            let! consumer = stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("order_processor"))

            do!
                consumer.FetchAsync<string>(opts = new NatsJSFetchOpts(MaxMsgs = 1))
                |> TaskSeq.iter (fun jmsg ->
                    f (jmsg.Data |> JsonSerializer.Deserialize<Repo>)
                    jmsg.AckAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously)

            printfn "after fetch async"

        }

    let triggerReleaseDownload (user: string) (repo: string) =
        task {
            let repo =
                { user = user
                  repo = repo
                  kind = Github
                  checksums = [] }

            do! publish repo
        }
