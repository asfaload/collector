open Asfaload.Collector.Index
open System

let main gitDir =
    generateChecksumsList gitDir (Some DateTimeOffset.UtcNow) (Some DateTimeOffset.UtcNow)

let args = System.Environment.GetCommandLineArgs()

let directory =
    if args |> Seq.length < 2 then
        System.Environment.GetEnvironmentVariable("BASE_DIR")
    else

        args[1]

main directory
