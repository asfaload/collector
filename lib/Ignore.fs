namespace Asfaload.Collector

open System.IO
open System

module Ignore =
    let githubIgnoreFile =
        System.Environment.GetEnvironmentVariable("GITHUB_IGNORE_FILE") |> Option.ofObj

    // keep last time the github ignore file was processed
    let mutable githubIgnoreUpdatedAt: DateTime option = None

    let processGithubIgnoreFile (ignoreFile: string option) =
        printfn "Loading github ignore file %A" ignoreFile
        githubIgnoreUpdatedAt <- githubIgnoreFile |> Option.map (fun p -> FileInfo(p).LastWriteTimeUtc)

        ignoreFile
        |> Option.bind (fun path ->
            if File.Exists path then
                Some <| (File.ReadLines path)
            else
                printfn "Ignore file %s does not exist" path
                None)


    let isIgnored (blockSeq: string seq option) (value: string) =
        printfn "Evaluating if %s is ignored" value

        blockSeq
        |> Option.bind (fun s ->
            s
            |> Seq.map (fun s -> s, System.Text.RegularExpressions.Regex(s))
            |> Seq.map (fun (s, regex) -> s, regex.Match(value).Success)
            |> Seq.tryFind (fun (_s, b) -> b)
            |> Option.map (fun (s, b) ->
                printfn "%s isgnored as it matches regexp `%s`" value s
                b))
        |> Option.defaultValue false


    // Functions used in prod
    //
    // Initialised to None as it is updated when used
    let mutable githubIgnored = None

    let isGithubIgnored user repo =
        // Load/reload ignore file if needed
        githubIgnoreFile
        |> Option.map FileInfo
        |> Option.iter (fun info ->
            match githubIgnoreUpdatedAt with
            | None ->
                printfn "First loading of github ignore file"
                githubIgnored <- processGithubIgnoreFile githubIgnoreFile
            | Some dt ->
                if info.LastWriteTimeUtc > dt then
                    printfn "reloading github ignore file"
                    githubIgnored <- processGithubIgnoreFile githubIgnoreFile)

        isIgnored githubIgnored $"{user}/{repo}"
