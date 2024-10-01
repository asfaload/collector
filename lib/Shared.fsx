#r "nuget: FSharp.SystemTextJson, 1.3.13"
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
