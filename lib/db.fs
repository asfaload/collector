// Modules defining access to sqlite databases.
namespace Asfaload.Collector.DB

open DbFun.Core
open DbFun.Core.Builders
open System.Data
open System.Data.SQLite
open DbFun.Core.Models
open DbFun.Core.Sqlite
open System

module Sqlite =
    let createConnection () : IDbConnection =
        new SQLiteConnection(
            $"""Data Source={Environment.GetEnvironmentVariable("REPOS_DB")}; PRAGMA journal_mode=WAL"""
        )

    let config = QueryConfig.Default(createConnection).SqliteDateTimeAsString()
    let query = QueryBuilder(config)
    let run f = DbCall.Run(createConnection, f)

    type DateModifier =
        | LastHour
        | LastDay
        | LastWeek
        | LastMonth

        override self.ToString() =
            match self with
            | LastHour -> "-1 hours"
            | LastDay -> "-1 days"
            | LastWeek -> "-7 days"
            | LastMonth -> "-1 months"

module Repos =
    open Sqlite
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

    // create table request_logs(id INTEGER PRIMARY KEY, hoster text, user text, repo text,time text DEFAULT CURRENT_TIMESTAMP, request text);
    type RequestLog =
        { id: int
          hoster: RepoKind
          user: string
          repo: string
          time: DateTimeOffset }




    let getUnsubscribedRepos =
        // FIXME: support usernames only with digits. Due to DbFun
        query.Sql(
            "select id,hoster,user,repo,subscribed from repos where subscribed=false and id is not 1314 limit 10;",
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

        // `or ignore` to ignore unique constraints errors
        query.Sql
            ("insert or ignore into repos(hoster,user,repo) VALUES ('github', @user, @repo) returning *",
             Params.Record<Repo>(),
             Results.List<Repo>())
            repo

    let seen (user: string) (repo: string) =
        let repo =
            { id = 0
              hoster = Github
              user = user
              repo = repo
              subscribed = false }

        // `or ignore` to ignore unique constraints errors
        query.Sql
            ("insert or ignore into repos_seen(hoster,user,repo) VALUES ('github', @user, @repo)",
             Params.Record<Repo>(),
             Results.Unit)
            repo

    let isKnown (user: string) (repo: string) =
        let repo =
            { id = 0
              hoster = Github
              user = user
              repo = repo
              subscribed = false }

        query.Sql
            ("select count(*) from repos_seen where user=@user and repo=@repo",
             Params.Record<Repo>(),
             Results.Single<int64>())
            repo



open Asfaload.Collector.User

module User =

    let getProfile user =
        async.Return { user = user; profile = OpenSource }

module Rates =
    open Sqlite

    let periodRequests (modifier: DateModifier) (hoster: string) (user: string) (repo: string) =
        query.Sql
            ($"select count(*) from request_logs where user=@user and repo=@repo and hoster=@hoster and datetime(time) >= datetime('now', '{modifier.ToString()}')",
             Params.Tuple<string, string, string>("hoster", "user", "repo"),
             Results.Single<int64> "")
            (hoster, user, repo)

    let hourlyRequests hoster user repo =
        periodRequests LastHour hoster user repo

    let weeklyRequests hoster user repo =
        periodRequests LastWeek hoster user repo

    let monthlyRequests hoster user repo =
        periodRequests LastMonth hoster user repo

    let individualRateQuery (filter: string) (limit: int) =
        $"select count(*)<{limit} as ok From request_logs where datetime(time)>=datetime('now','{filter}') and user=@user and request=@request"


    let checkRate (p: UserProfile) (request: string) =
        async {
            let limits = p.profile.limits ()

            // We take the union of 3 queries: faily, weekly and monhtly.
            // Beware the union all: it is required of duplicates will be removed, which we do not want here.
            // Each query will return one of the rate is respected, and we sum them and check all where ok (i.e. 1) giving a total of 3
            let sql =
                $"""select sum(ok) as ok from (
                  {individualRateQuery (DateModifier.LastDay.ToString()) (limits.releases.day |> Option.defaultValue 1000)}
                  UNION ALL
                  {individualRateQuery (DateModifier.LastWeek.ToString()) (limits.releases.week |> Option.defaultValue 1000)}
                  UNION ALL
                  {individualRateQuery (DateModifier.LastMonth.ToString()) (limits.releases.month |> Option.defaultValue 1000)}
                  )
              """

            let! result =
                query.Sql
                    (sql, Params.Tuple<string, string>("user", "request"), Results.Single<int64>())
                    (p.user, request)
                |> Sqlite.run

            return result = 3
        }

    let recordRequest hoster user repo request =
        let sql =
            "insert into request_logs(hoster,user, repo, request) VALUES (@hoster, @user, @repo, @request)"

        query.Sql
            (sql, Params.Tuple<string, string, string, string>("hoster", "user", "repo", "request"), Results.Unit)
            (hoster, user, repo, request)
        |> Sqlite.run
