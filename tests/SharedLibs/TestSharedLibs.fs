module tests.GenerateIndex

open Asfaload.Collector.Ignore
open NUnit.Framework
open FsUnit

open System.IO

[<SetUp>]
let Setup () = ()

[<OneTimeSetUp>]
let OneTimeSetup () =
    System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener())
    |> ignore

[<OneTimeTearDown>]
let OneTimeTearDown () = System.Diagnostics.Trace.Flush()


[<Test>]
let test_isIgnored () =

    isIgnored None "asfaload/asfald" |> should equal false
    isIgnored (Some Seq.empty) "asfaload/asfald" |> should equal false

    // Attention, uses partial match!
    //---
    let regexps = [| "a" |]

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/asfald" |> should equal true

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/checksum"
    |> should equal true

    isIgnored (Some <| (Seq.ofArray regexps)) "patate/python" |> should equal true
    isIgnored (Some <| (Seq.ofArray regexps)) "pate/brisee" |> should equal true
    isIgnored (Some <| (Seq.ofArray regexps)) "poot/gebroken" |> should equal false
    //---
    let regexps = [| "asfaload/checksums" |]

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/asfald"
    |> should equal false

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/checksum"
    |> should equal false

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/checksumster"
    |> should equal true

    //--- supports .*
    let regexps = [| "asfaload/checksums"; "asfaload/.*" |]
    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/asfald" |> should equal true

    isIgnored (Some <| (Seq.ofArray regexps)) "github/electron"
    |> should equal false

    //---  Ignore all usernames starting with asfa
    let regexps = [| "asfaload/checksums"; "asfa.*" |]
    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/asfald" |> should equal true

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaromual/website"
    |> should equal true
    //--- Ignore all repos named exactly v2play (note $)
    let regexps = [| "asfaload/checksums"; ".*/v2play$" |]

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/checksums"
    |> should equal true

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/asfald"
    |> should equal false

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/v2play" |> should equal true

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/v2player"
    |> should equal false
    //--- Ignore all  usernames starting with a `a`
    let regexps = [| "asfaload/checksums"; "^a.*" |]

    isIgnored (Some <| (Seq.ofArray regexps)) "asfaload/checksums"
    |> should equal true

    isIgnored (Some <| (Seq.ofArray regexps)) "beastfaload/checksums"
    |> should equal false


[<Test>]
let test_ignoreFromFile () =

    let validateProcessedGithubIgnore githubIgnore =
        isIgnored githubIgnore "asfaload/checksums" |> should equal true
        isIgnored githubIgnore "astalock/checksums" |> should equal false
        isIgnored githubIgnore "astalock/bibol" |> should equal true
        isIgnored githubIgnore "evilCorp/welcome" |> should equal true
        isIgnored githubIgnore "evilcorp/welcome" |> should equal false
        isIgnored githubIgnore "zzevilcorp/welcome" |> should equal true
        isIgnored githubIgnore "evilcorpzz/welcome" |> should equal true
        isIgnored githubIgnore "evilcorp/zzwelcome" |> should equal true
        isIgnored githubIgnore "evilcorp/welcomezz" |> should equal true

    let githubIgnore = processGithubIgnoreFile (Some "/tmp/inexisting/file/ignore.txt")
    isIgnored githubIgnore "asfaload/checksums" |> should equal false

    let ignoreFilePath = System.IO.Path.GetTempFileName()
    let ignoredPatterns = [| "asfa.*/.*"; ".*/bibol"; "evilCorp/.*"; "zz" |]
    File.WriteAllLines(ignoreFilePath, ignoredPatterns)

    let githubIgnore = processGithubIgnoreFile (Some ignoreFilePath)
    validateProcessedGithubIgnore githubIgnore

    let githubIgnoreFromFile =
        processGithubIgnoreFile (Some "fixtures/sampleGithubIgnore.txt")

    validateProcessedGithubIgnore githubIgnoreFromFile

[<Test>]
let test_ignoreFileReload () =
    let ignoreFilePath = System.IO.Path.GetTempFileName()
    let ignoredPatterns = [| "asfa.*/.*" |]
    File.AppendAllLines(ignoreFilePath, ignoredPatterns)
    let githubIgnore = processGithubIgnoreFile (Some ignoreFilePath)
    isIgnored githubIgnore "asfaload/checksums" |> should equal true
    isIgnored githubIgnore "astalock/bibol" |> should equal false
    isIgnored githubIgnore "evilCorp/welcome" |> should equal false
    isIgnored githubIgnore "zzevilcorp/welcome" |> should equal false
    isIgnored githubIgnore "evilcorp/welcome" |> should equal false
    File.AppendAllLines(ignoreFilePath, [| ".*/bibol" |])
    isIgnored githubIgnore "asfaload/checksums" |> should equal true
    isIgnored githubIgnore "astalock/bibol" |> should equal true
    isIgnored githubIgnore "evilCorp/welcome" |> should equal false
    isIgnored githubIgnore "zzevilcorp/welcome" |> should equal false
    isIgnored githubIgnore "evilcorp/welcome" |> should equal false
    File.AppendAllLines(ignoreFilePath, [| "evilCorp/.*" |])
    isIgnored githubIgnore "asfaload/checksums" |> should equal true
    isIgnored githubIgnore "astalock/bibol" |> should equal true
    isIgnored githubIgnore "evilCorp/welcome" |> should equal true
    isIgnored githubIgnore "zzevilcorp/welcome" |> should equal false
    isIgnored githubIgnore "evilcorp/welcome" |> should equal false
    File.AppendAllLines(ignoreFilePath, [| "zz" |])
    isIgnored githubIgnore "asfaload/checksums" |> should equal true
    isIgnored githubIgnore "astalock/bibol" |> should equal true
    isIgnored githubIgnore "evilCorp/welcome" |> should equal true
    isIgnored githubIgnore "zzevilcorp/welcome" |> should equal true
    isIgnored githubIgnore "evilcorp/welcome" |> should equal false
