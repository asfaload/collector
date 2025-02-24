namespace Asfaload.Collector

open System.IO
open System

module Ignore =
    let githubIgnoreFile =
        System.Environment.GetEnvironmentVariable("GITHUB_IGNORE_FILE") |> Option.ofObj

    // keep last time the github ignore file was processed
    let mutable githubIgnoreUpdatedAt: DateTimeOffset option = None

    let processGithubIgnoreFile (ignoreFile: string option) =
        printfn "Loading github ignore file %A" ignoreFile
        githubIgnoreUpdatedAt <- Some <| DateTimeOffset.Now

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
    let githubIgnored = processGithubIgnoreFile githubIgnoreFile

    let isGithubIgnored user repo =
        isIgnored githubIgnored $"{user}/{repo}"
