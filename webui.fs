open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.Json
open Asfaload.Collector.DB
open Asfaload.Collector.ChecksumHelpers
open System.Runtime.Serialization
open Fli
open Asfaload.Collector.DB

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

let suggestForm =
    """
<html>
<head>
</head>
<body>
<form method="post" action="/register_suggestion">
user/repo: <input type="text" name="repo" placeholder="user/repo"></input><br/>
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


let parseReleaseActionBody (request: HttpRequest) =
    request.rawForm
    |> System.Text.Encoding.ASCII.GetString
    |> Asfaload.Collector.ReleaseCallbackBody.Parse

let validateJwtAndBody (ctx: HttpContext) =
    async.Return(

        ctx.request.header "Authorization"
        |> (function
        | Choice1Of2 h -> Some h
        | Choice2Of2 _e -> None)
        // if token is invalid, authoriseActionCall will return None
        // which will stop the pipeline
        |> Option.bind authoriseActionCall
        // Log outcome
        |> Option.map (fun r ->
            printfn "jwt was valid, request if from %s" (r.Repository)
            r)
        |> Option.orElseWith (fun () ->
            printfn "jwt was INVALID"
            None)
        // Check Repo in body corresponds to repo in jwt
        |> Option.bind (fun payload ->
            let body = parseReleaseActionBody ctx.request

            if payload.Repository = body.Repository.FullName then
                printfn "repository in body validated with jwt"
                Some payload
            else
                printfn "repository in body different from jwt!!"
                None)
        // If the call was Successful, we return the wrapped context to
        // continue the pipeline
        |> Option.map (fun _ -> ctx)
    )


open Asfaload.Collector.Limits
open Asfaload.Collector.User

let app: WebPart =
    choose
        [ GET >=> path "/" >=> OK form
          GET >=> path "/suggest" >=> OK suggestForm
          POST
          >=> path "/register_suggestion"
          >=> request (fun req ->
              match req["repo"] with
              | Some repo ->
                  printfn "SUGGESTION: %s" repo
                  Successful.OK $"""Thanks for the suggestion, it will be handled shortly!<br/>{suggestForm}"""
              | None -> Suave.RequestErrors.BAD_REQUEST "Please provide the github repo to add to the Asfaload mirror")


          POST
          >=> path "/add"
          >=> request (fun req ->
              match req["auth"] with
              | Some "1348LLN" ->

                  let res =
                      req["repo"]
                      |> Option.map (fun r -> r.Split("/") |> (fun a -> (a[0], a[1])))
                      |> Option.bind (fun (user, repo) ->
                          try
                              printfn "will create repo %s/%s" user repo
                              let created = Repos.create user repo |> Sqlite.run |> Async.RunSynchronously

                              printfn "will trigger download of release"

                              Asfaload.Collector.Queue.triggerRepoReleaseDownload user repo
                              |> Async.AwaitTask
                              |> Async.RunSynchronously

                              printfn "Created = %A" created
                              Some created
                          with e ->
                              printfn "Exception inserting new repo: %s" e.Message
                              None)

                  match res with
                  | Some [ r ] -> Successful.OK $"""Insert repo {sprintf "%A" r}<br/>{form}"""
                  | Some [] -> Successful.OK $"""Repo was already known<br/>{form}"""
                  | _ -> Suave.ServerErrors.INTERNAL_ERROR $"An error occurred<br/>{form}"
              | _ -> Suave.RequestErrors.FORBIDDEN "Provide authentication code")
          POST
          >=> path "/v1/github_action_register_release"
          >=> choose
                  [ validateJwtAndBody
                    >=> (fun (ctx: HttpContext) ->
                        async {

                            let call = "github_action_register_release"
                            let req = ctx.request
                            let body = parseReleaseActionBody req
                            let user = body.Repository.Owner.Login
                            let repo = body.Repository.Name

                            let! userProfile = User.getProfile user
                            let! requestAccepted = Rates.checkRate userProfile call

                            if requestAccepted then
                                printfn "accepted"
                                do! Rates.recordAcceptedRequest "github" user repo call

                                do!
                                    Asfaload.Collector.Queue.publishCallbackRelease
                                        user
                                        repo
                                        (req.rawForm |> System.Text.Encoding.ASCII.GetString)
                                    |> Async.AwaitTask

                                return! OK "Ok" ctx
                            else
                                do! Rates.recordRejectedRequest "github" user repo call
                                printfn "Request to github_action_register_release rejected for user %s/%s" user repo
                                return! RequestErrors.TOO_MANY_REQUESTS "Over limit" ctx


                        })
                    RequestErrors.UNAUTHORIZED "Invalid Jwt" ]
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
                          let! created = Repos.create repo.user repo.repo |> Sqlite.run
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
              }
              |> Async.RunSynchronously))
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
                  | HasChecksum ->
                      let created =
                          Repos.create info.user info.repo |> Sqlite.run |> Async.RunSynchronously

                      if created |> List.length > 0 then
                          Asfaload.Collector.Queue.triggerRepoReleaseDownload info.user info.repo
                          |> Async.AwaitTask
                          |> Async.RunSynchronously

                          return
                              { status = "ADDED"
                                msg = "Added, repo is now tracked" }
                      else
                          return
                              { status = "KNOWN"
                                msg = "Repo was known" }
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
startWebServer cfg app
