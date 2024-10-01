namespace Asfaload.Collector

type RepoKind =
    | Github
    | Gitlab

type Repo =
    { kind: RepoKind
      user: string
      repo: string
      checksums: string list }
