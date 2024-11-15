open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Json
open Asfaload.Collector.DB
open Asfaload.Collector.ChecksumHelpers
open System.Runtime.Serialization
open Fli

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



let jwt_validate = System.Environment.GetEnvironmentVariable("JWT_VALIDATOR_PATH")

let authoriseActionCall (jwt: string) : (Asfaload.Collector.JwtPayload.Root option) =
    let output =
        cli {
            Exec jwt_validate
            Arguments [ jwt ]
        }
        |> Command.execute

    match output.Error with
    | Some error ->
        printfn "jwt validation error:\n%s" error
        None
    | None -> output |> Output.toText |> Asfaload.Collector.JwtPayload.Parse |> Some


let validateJwt (ctx: HttpContext) =
    async.Return(
        ctx.request["Authorization"]
        // if token is invalid, authoriseActionCall will return None
        // which will stop the pipeline
        |> Option.bind authoriseActionCall
        // If the call was Successful, we return the wrapped context to
        // continue the pipeline
        |> Option.map (fun _ -> ctx)
    )


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

                              Asfaload.Collector.Queue.triggerRepoReleaseDownload user repo
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
          POST
          >=> path "/v1/github_action_register_release"
          >=> validateJwt
          >=> request (fun req ->

              let body =
                  req.rawForm
                  |> System.Text.Encoding.ASCII.GetString
                  |> Asfaload.Collector.ReleaseCallbackBody.Parse

              let user = body.Repository.Owner.Login
              let repo = body.Repository.Name

              Asfaload.Collector.Queue.publishCallbackRelease
                  user
                  repo
                  (req.rawForm |> System.Text.Encoding.ASCII.GetString)
              |> Async.AwaitTask
              |> Async.RunSynchronously

              Successful.OK "Ok"

          )
          // Post with curl:
          // curl -X POST -d '{"user":"asfaload","repo":"asfald"}' https://collector.asfaload.com/v1/register_github_release
          POST
          >=> path "/v1/register_github_release"
          >=> (mapJson (fun (repo: RepoToRegister) ->
              async {
                  // FIXME: add DoS protection:
                  // - We can authenticate requests coming from a github action:
                  // https://gal.hagever.com/posts/authenticating-github-actions-requests-with-github-openid-connect
                  // https://github.com/Schniz/benchy-action
                  // https://stackoverflow.com/questions/58601556/how-to-validate-jwt-token-using-jwks-in-dot-net-core
                  // https://stackoverflow.com/questions/40623346/how-do-i-validate-a-jwt-using-jwtsecuritytokenhandler-and-a-jwks-endpoint/64274938#64274938
                  // https://www.nuget.org/packages/microsoft.identitymodel.jsonwebtokens/
                  // https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/about-security-hardening-with-openid-connect#adding-permissions-settings
                  // https://stackoverflow.com/questions/72183048/what-is-the-permission-scope-of-id-token-in-github-action
                  //
                  // - limit number of releases a project can do
                  let work =
                      async {
                          let! created = Repos.create repo.user repo.repo |> Repos.run
                          do! Asfaload.Collector.Queue.triggerRepoReleaseDownload repo.user repo.repo
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
                  // FIXME: add DoS protection:
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
startWebServer cfg app
startWebServer cfg app
