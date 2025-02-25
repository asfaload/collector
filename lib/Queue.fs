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

    let getNextAndAck (streamName: string) (subject: string) (configName: string) (timeout: TimeSpan) =
        task {
            let! stream = getStream streamName [| subject |]
            let consumerConfig = new ConsumerConfig(configName)
            consumerConfig.FilterSubject <- subject
            let! consumer = stream.CreateOrUpdateConsumerAsync(consumerConfig)

            let! next = consumer.NextAsync<string>(null, NatsJSNextOpts(Expires = Nullable<TimeSpan>(timeout)))
            //consumer.NextAsync<string>()

            if next.HasValue then
                do! next.Value.AckAsync()
                return Some(next.Value.Data)
            else
                return None
        }

    // Durable consumers are persistent and created at the NATS side. So even when
    // the program exits, the consumer stays. This allows to delete a consumer,
    // and is particularly useful in tests.
    let deleteConsumerIfExists (streamName: string) (subjects: string array) (name: string) =
        task {
            let! stream = getStream streamName subjects

            try
                let! success = stream.DeleteConsumerAsync(name)
                return success
            with
            | :? NATS.Client.JetStream.NatsJSApiException as e ->
                // if the consumer is not found, consider it as deleted
                if e.Message = "consumer not found" then
                    return true
                else
                    raise e
                    return Unchecked.defaultof<_> ()
            | e ->
                raise e
                return Unchecked.defaultof<_> ()



        }

    let consumeRepoReleases (f: Repo -> unit) =
        task {
            let! stream = getStream "RELEASES" [| "releases.>" |]
            let! consumer = stream.CreateOrUpdateConsumerAsync(new ConsumerConfig("releases_processor"))

            printfn
                "The release_processor consumer has these info:\ncreated at:%A\nName:%s\nPending msgs: %d"
                consumer.Info.Created
                consumer.Info.Name
                consumer.Info.NumPending

            do!
                consumer.FetchAsync<string>(opts = new NatsJSFetchOpts(MaxMsgs = 1, Expires = TimeSpan.FromSeconds(1)))
                |> TaskSeq.iter (fun jmsg ->
                    try

                        jmsg.Metadata
                        |> (fun d -> if d.HasValue then Some d.Value else None)
                        |> Option.iter (fun md -> printfn "Retrieved message with timestamp %A" md.Timestamp)

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

            printfn
                "The release_callback_processor consumer has these info:\ncreated at:%A\nName:%s\nPending msgs: %d"
                consumer.Info.Created
                consumer.Info.Name
                consumer.Info.NumPending

            do!
                consumer.FetchAsync<string>(opts = new NatsJSFetchOpts(MaxMsgs = 1, Expires = TimeSpan.FromSeconds(1)))
                |> TaskSeq.iter (fun jmsg ->

                    jmsg.Metadata
                    |> (fun d -> if d.HasValue then Some d.Value else None)
                    |> Option.iter (fun md -> printfn "Retrieved message with timestamp %A" md.Timestamp)

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
