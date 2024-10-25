#load "lib/Index.fsx"
open Asfaload.Collector.Index
let main gitDir = generateChecksumsList gitDir

let args = fsi.CommandLineArgs

let directory =
    if args |> Seq.length < 2 then
        System.Environment.GetEnvironmentVariable("BASE_DIR")
    else
        args[1]

main directory
