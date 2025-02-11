namespace Asfaload.Collector

module Queue =
    open System
    open System.Text.Json


    open NATS.Client.JetStream
    open NATS.Client.JetStream.Models
    open NATS.Net
    open FSharp.Control
    open FSharp.Data


    let mutable cachedContext: INatsJSContext option = None
    //let mutable cachedStream: INatsJSStream option = None
    //let mutable cachedContext: Map<string, INatsJSContext> = Map.empty
    let mutable cachedStream: Map<string, INatsJSStream> = Map.empty

    let getContext () =
        match cachedContext with
        | Some v -> v
        | None ->
            let url = Environment.GetEnvironmentVariable("NATS_URL")
            let nc = new NatsClient(url)
            let ctx = nc.CreateJetStreamContext()
            cachedContext <- Some ctx
            ctx

    let getStream (streamName: string) (subjects: string array) =
        task {

            match cachedStream.TryFind streamName with
            | Some s -> return s
            | None ->
                let js = getContext ()

                let! s =
                    js.CreateStreamAsync(
                        new StreamConfig(streamName, subjects = subjects, Retention = StreamConfigRetention.Workqueue)
                    )

                cachedStream <- cachedStream.Add(streamName, s)
                return s

        }

    let publishToQueue (streamName: string) (subjects: string array) (q: string) (serialised: string) =
        task {
            // Need to initialise stream here
            let! stream = getStream streamName subjects
            let js = getContext ()
            let! ack = js.PublishAsync(q, serialised)
            ack.EnsureSuccess()

        }

    let publishRepoRelease (repo: Repo) =
        publishToQueue
            "RELEASES"
            [| "releases.>" |]
            ($"releases.new.{repo.user}/{repo.repo}")
            (repo |> JsonSerializer.Serialize)

    let publishCallbackRelease user repo (callbackBody: string) =
        publishToQueue
            "RELEASES_CALLBACK"
            [| "releases_callback.>" |]
            ($"releases_callback.new.{user}/{repo}")
            callbackBody

    let getNextAndAck (streamName: string) (subjects: string array) (configName: string) (timeout: TimeSpan) =
        task {
            let! stream = getStream streamName subjects
            let! consumer = stream.CreateOrUpdateConsumerAsync(new ConsumerConfig(configName))

            let! next = consumer.NextAsync<string>(null, NatsJSNextOpts(Expires = Nullable<TimeSpan>(timeout)))
            //consumer.NextAsync<string>()

            if next.HasValue then
                do! next.Value.AckAsync()
                return Some(next.Value.Data)
            else
                return None
        }

    let consumeRepoReleases (f: Repo -> unit) =
        task {
            let! stream = getStream "RELEASES" [| "releases.>" |]
            let! consumer = stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("releases_processor"))

            do!
                consumer.FetchAsync<string>(opts = new NatsJSFetchOpts(MaxMsgs = 1))
                |> TaskSeq.iter (fun jmsg ->
                    try
                        f (jmsg.Data |> JsonSerializer.Deserialize<Repo>)
                        jmsg.AckAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously
                    with e ->
                        printfn "got error: %s" e.StackTrace
                        printfn "will still dispense of message %s" jmsg.Data
                        jmsg.AckAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously

                )

            printfn "after fetch async"

        }

    let consumeCallbackRelease (f: ReleaseCallbackBody.Root -> unit) =
        task {
            let! stream = getStream "RELEASES_CALLBACK" [| "releases_callback.>" |]
            let! consumer = stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("releases_callback_processor"))

            do!
                consumer.FetchAsync<string>(opts = new NatsJSFetchOpts(MaxMsgs = 1))
                |> TaskSeq.iter (fun jmsg ->
                    f (jmsg.Data |> ReleaseCallbackBody.Parse)
                    jmsg.AckAsync().AsTask() |> Async.AwaitTask |> Async.RunSynchronously)

            printfn "after fetch async"

        }

    let triggerRepoReleaseDownload (user: string) (repo: string) =
        task {
            let repo =
                { user = user
                  repo = repo
                  kind = Github
                  checksums = [] }

            do! publishRepoRelease repo
        }
