// Modules defining access to sqlite databases.
#r "nuget: DbFun.Core, 1.1.0"
#r "nuget: System.Data.SQLite, 1.0.119"
#load "Shared.fsx"
namespace Asfaload.Collector.DB

open DbFun.Core
open DbFun.Core.Builders
open System.Data
open System.Data.SQLite
open DbFun.Core.Models
open DbFun.Core.Sqlite
open System

module Repos =
    // create table repos(id INTEGER PRIMARY KEY, hoster string, user string, repo string, subscribed bool default 0, last_release text, UNIQUE(hoster,user,repo));
    type RepoKind =
        | [<UnionCaseTag("github")>] Github
        | [<UnionCaseTag("gitlab")>] Gitlab

    type Repo =
        { id: int
          hoster: RepoKind
          user: string
          repo: string
          subscribed: bool
        // last_release causes trouble with DbFun.....
        }

    let createConnection () : IDbConnection =
        new SQLiteConnection($"""Data Source={Environment.GetEnvironmentVariable("REPOS_DB")}""")

    let config = QueryConfig.Default(createConnection).SqliteDateTimeAsString()
    let query = QueryBuilder(config)
    let run f = DbCall.Run(createConnection, f)


    let getUnsubscribedRepos =
        query.Sql(
            "select id,hoster,user,repo,subscribed from repos where subscribed=false;",
            Params.Unit,
            Results.List<Repo>()
        )

    let setSubscribed =
        query.Sql("update repos set subscribed=true where id=@id", Params.Record<Repo>(), Results.Unit)

    let create (user: string) (repo: string) =
        let repo =
            { id = 0
              hoster = Github
              user = user
              repo = repo
              subscribed = false }

        query.Sql
            ("insert into repos(hoster,user,repo) VALUES ('github', @user, @repo) returning *",
             Params.Record<Repo>(),
             Results.List<Repo>())
            repo
