// Script to subscribe to new releases of projects. It loops and looks in the table repos
// in the sqlite database $REPOS_DB for entries with subscribed=false.
// It uses Playwright to subsribe via the web interface, as it is the only way
// to subscribe to only releases notifications.
#r "nuget: FsHttp"
#r "nuget: Microsoft.Playwright, 1.47.0"
#r "nuget: FSharp.SystemTextJson, 1.3.13"
#r "nuget: System.Data.SQLite, 1.0.119"
#load "lib/db.fsx"
// Getting playwright installed:
// // Create a new project ni which you install playwright
// dotnet new console -lang F#
// dotnet add package Microsoft.Playwright --version 1.47.0
// // install powershell
// mise use powershell-core
// // Then run the script installing browsers
// pwsh bin/Debug/net8.0/playwright.ps1 install

open System
open System.IO

open Microsoft.Playwright
open Asfaload.Collector.DB

let browserStatePath = "private/auth/state.json"
let fromEnv = Environment.GetEnvironmentVariable

let subscribeTo (page: IPage) (username: string) (repo: string) =
    task {
        let! _response = page.GotoAsync($"https://github.com/{username}/{repo}")

        do!
            page
                .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Watch"))
                .ClickAsync()

        do! page.GetByText("Custom", PageGetByTextOptions(Exact = true)).ClickAsync()

        do!
            page
                .GetByRole(AriaRole.Checkbox, PageGetByRoleOptions(Name = "Releases", Exact = true))
                .CheckAsync()

        do!
            page
                .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Apply", Exact = true))
                .ClickAsync()

        return true

    }
    |> Async.AwaitTask

let firefoxAsync () =
    task {
        let! playwright = Playwright.CreateAsync()
        let firefox = playwright.Firefox
        let! browser = firefox.LaunchAsync(BrowserTypeLaunchOptions(Headless = true))
        return browser

    }

let firefoxPage () =
    task {
        printfn "starting browser"
        let! browser = firefoxAsync ()

        // The browser state initialisation is awkward. We do it ourselves if no state was found.
        if not (File.Exists(browserStatePath)) then
            File.WriteAllText(browserStatePath, """{"cookies":[],"origins":[]}""")

        let! context = browser.NewContextAsync(BrowserNewContextOptions(StorageStatePath = browserStatePath))
        let! page = context.NewPageAsync()
        return page, context
    }

let login (context: IBrowserContext) (page: IPage) =
    task {
        let! _response = page.GotoAsync("https://github.com/login")
        // Interact with login form
        do!
            page
                .GetByLabel("Username or email address")
                .FillAsync(fromEnv "WATCHER_USERNAME")

        do! page.GetByLabel("Password").FillAsync(fromEnv "WATCHER_PASSWORD")


        do!
            page
                .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Sign in", Exact = true))
                .ClickAsync()

        // This saves the contect to disk, what a weird api....
        let! state = context.StorageStateAsync(BrowserContextStorageStateOptions(Path = "private/auth/state.json"))
        return page
    }

// Checks if user is logged in. Changes url of the page!
let isLoggedIn (page: IPage) =
    task {
        let! _response = page.GotoAsync("https://github.com/")
        //return! page.Locator("strong").GetByText(("Dashboard")).IsVisibleAsync()
        return!
            page
                .GetByRole(AriaRole.Heading, PageGetByRoleOptions(Name = "Dashboard"))
                .IsVisibleAsync()
    }

let rec subscriptionLoop (page: IPage) =
    async {
        let! unsubscribedRepos = Repos.getUnsubscribedRepos () |> Repos.run

        let! _r =
            unsubscribedRepos
            |> List.map (fun r -> subscribeTo page r.user r.repo)
            |> Async.Sequential

        let! _r =
            unsubscribedRepos
            |> List.map (fun r -> Repos.setSubscribed r |> Repos.run)
            |> Async.Sequential

        printfn "%A Sleeping before next subscription loop" DateTime.Now
        do! Async.Sleep 5000
        return! subscriptionLoop page

    }

let main () =
    task {
        let! page, context = firefoxPage ()

        match! isLoggedIn (page) with
        | true -> printfn "logged in!"
        | false ->
            printfn "logging in"
            let! _ = login context page
            ()

        let! _r = subscriptionLoop page


        return 0
    }

main () |> Async.AwaitTask |> Async.RunSynchronously
////  The REST API only allows to subsribe to all events, not only releases :-(
//open FsHttp
//let user = "jesseduffield"
//let repo = "lazygit"
//
//http {
//    PUT $"https://api.github.com/repos/{user}/{repo}/subscription"
//    AcceptLanguage "en-US"
//    Accept "application/vnd.github+json"
//    UserAgent "rbauduin-test"
//    AuthorizationBearer(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
//    header "X-GitHub-Api-Version" "2022-11-28"
//    body
//    json """{"subscribed":true,"ignored":false}"""
//
//}
//|> Request.send
//}
//|> Request.send
