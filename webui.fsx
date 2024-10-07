#r "nuget: Suave, 2.6.2"
#r "nuget: System.Data.SQLite, 1.0.119"
#load "lib/db.fsx"

open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Asfaload.Collector.DB


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
          >=> path "/add"
          >=> request (fun req ->
              match req["auth"] with
              | Some "1348LLN" ->

                  let res =
                      req["repo"]
                      |> Option.map (fun r -> r.Split("/") |> (fun a -> (a[0], a[1])))
                      |> Option.map (fun (user, repo) ->
                          try
                              Repos.create user repo |> Repos.run |> Async.RunSynchronously
                          with e ->
                              printfn "Exception inserting new repo: %s" e.Message
                              [])

                  match res with
                  | Some [ r ] -> Successful.OK $"""Insert repo {sprintf "%A" r}<br/>{form}"""
                  | _ -> Suave.ServerErrors.INTERNAL_ERROR $"An error occurred<br/>{form}"
              | _ -> Suave.RequestErrors.FORBIDDEN "Provide authentication code")

          RequestErrors.NOT_FOUND "Page not found." ]

let cfg =
    { defaultConfig with
        bindings = [ HttpBinding.createSimple HTTP "0.0.0.0" 8080 ]
        listenTimeout = System.TimeSpan.FromMilliseconds 3000. }

startWebServer cfg app
