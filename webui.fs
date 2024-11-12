open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Json
open Asfaload.Collector.DB
open Asfaload.Collector.ChecksumHelpers
open System.Runtime.Serialization

[<DataContract>]
type RepoToRegister =
    { [<field: DataMember(Name = "user")>]
      user: string
      [<field: DataMember(Name = "repo")>]
      repo: string }

[<DataContract>]
type RegisterReponse =
    { [<field: DataMember(Name = "status")>]
      status: string
      [<field: DataMember(Name = "msg")>]
      msg: string }

let form =
    """
<html>
<head>
</head>
<body>
<form method="post" action="/add">
user/repo: <input type="text" name="repo" placeholder="user/repo"></input><br/>
<input type="text" name="auth"></input>
<button>Submit</button>
</form>

</body>
"""

let app: WebPart =
    choose
        [ GET >=> path "/" >=> OK form
          POST
          >=> path "/notify_github_release"
          >=> request (fun req ->
              match req["auth"] with
              | Some "1348LLN" ->

                  let res =
                      req["repo"]
                      |> Option.map (fun r -> r.Split("/") |> (fun a -> (a[0], a[1])))
                      |> Option.map (fun (user, repo) ->
                          try
                              let created = Repos.create user repo |> Repos.run |> Async.RunSynchronously

                              Asfaload.Collector.Queue.triggerReleaseDownload user repo
                              |> Async.AwaitTask
                              |> Async.RunSynchronously

                              created
                          with e ->
                              printfn "Exception inserting new repo: %s" e.Message
                              [])

                  match res with
                  | Some [ r ] -> Successful.OK $"""Insert repo {sprintf "%A" r}<br/>{form}"""
                  | _ -> Suave.ServerErrors.INTERNAL_ERROR $"An error occurred<br/>{form}"
              | _ -> Suave.RequestErrors.FORBIDDEN "Provide authentication code")
          // Post with curl:
          // curl -X POST -d '{"user":"asfaload","repo":"asfald"}' https://collector.asfaload.com/v1/register_github_release
          POST
          >=> path "/v1/register_github_release"
          >=> (mapJson (fun (repo: RepoToRegister) ->
              async {
                  let work =
                      async {
                          let! created = Repos.create repo.user repo.repo |> Repos.run
                          do! Asfaload.Collector.Queue.triggerReleaseDownload repo.user repo.repo
                          return created
                      }

                  let! result = work |> Async.Catch

                  match result with
                  | Choice1Of2 _r ->
                      return
                          Successful.OK
                              $$"""{"status":"OK", "msg":"Registered release, checksums of last release from {{repo.user}}/{{repo.repo}} will be downloaded"}"""

                  | Choice2Of2 exc ->
                      printfn "%s:\n%s" (exc.Message) (exc.StackTrace)
                      return Suave.ServerErrors.INTERNAL_ERROR """{"status":"ERROR"} """
              }))
          // Post with curl:
          // curl -X POST -d '{"user":"asfaload","repo":"asfald"}' https://collector.asfaload.com/v1/track_github_repo
          POST
          >=> path "/v1/track_github_repo"
          >=> (mapJson (fun (info: RepoToRegister) ->
              async {
                  printfn "register %s/%s" info.user info.repo

                  match! getReleasesForRepo $"{info.user}/{info.repo}" with
                  | NoRelease ->
                      return
                          { status = "NO_RELEASE"
                            msg = "No release found" }
                  | NoChecksum ->
                      return
                          { status = "NO_CHECKSUM"
                            msg = "Release found, but no checksum file was found" }
                  | Added ->
                      return
                          { status = "ADDED"
                            msg = "Added, repo is now tracked" }
                  | Known ->
                      return
                          { status = "KNOWN"
                            msg = "Repo was already known" }
              }
              |> Async.RunSynchronously))

          RequestErrors.NOT_FOUND "Page not found." ]

let cfg =
    { defaultConfig with
        bindings = [ HttpBinding.createSimple HTTP "0.0.0.0" 8080 ]
        listenTimeout = System.TimeSpan.FromMilliseconds 3000. }

startWebServer cfg app
