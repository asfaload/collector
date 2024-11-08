// Script to subscribe to new releases of projects. It loops and looks in the table repos
// in the sqlite database $REPOS_DB for entries with subscribed=false.
// It uses Playwright to subsribe via the web interface, as it is the only way
// to subscribe to only releases notifications.
//
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
open OtpNet
open Asfaload.Collector.DB

let browserStatePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_STATE")
let fromEnv = Environment.GetEnvironmentVariable

let debugging () =
    not (isNull (Environment.GetEnvironmentVariable("DEBUG")))

let recorVideos () =
    not (isNull (Environment.GetEnvironmentVariable("VIDEOS")))

let subscribeTo (page: IPage) (username: string) (repo: string) =
    async {
        // Log url visited
        let url = $"https://github.com/{username}/{repo}"
        printfn "Visiting %s" url
        let! _response = page.GotoAsync(url) |> Async.AwaitTask

        do!
            page
                .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Watch"))
                .ClickAsync()
            |> Async.AwaitTask

        do!
            // We get by label as we had a repo with `<code>Custom</code> in its readme, causing trouble
            // with GetByText....`
            page.GetByLabel("Custom", PageGetByLabelOptions(Exact = true)).ClickAsync()
            |> Async.AwaitTask

        do!
            page
                .GetByRole(AriaRole.Checkbox, PageGetByRoleOptions(Name = "Releases", Exact = true))
                .CheckAsync()
            |> Async.AwaitTask

        do!
            page
                .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Apply", Exact = true))
                .ClickAsync()
            |> Async.AwaitTask

        return true

    }

let firefoxAsync () =
    task {
        let! playwright = Playwright.CreateAsync()
        let firefox = playwright.Firefox

        let! browser = firefox.LaunchAsync(BrowserTypeLaunchOptions(Headless = not (debugging ())))

        return browser

    }

let firefoxPage () =
    task {
        printfn "starting browser"
        let! browser = firefoxAsync ()

        // The browser state initialisation is awkward. We do it ourselves if no state was found.
        if not (File.Exists(browserStatePath)) then
            File.WriteAllText(browserStatePath, """{"cookies":[],"origins":[]}""")

        let options =
            if recorVideos () then
                BrowserNewContextOptions(StorageStatePath = browserStatePath, RecordVideoDir = "videos/")
            else
                BrowserNewContextOptions(StorageStatePath = browserStatePath)


        let! context = browser.NewContextAsync(options)

        let! page = context.NewPageAsync()
        return page, context
    }

let fill2FA (page: IPage) =
    task {
        let totp =
            Totp(Environment.GetEnvironmentVariable("TOTP_KEY") |> Base32Encoding.ToBytes)

        let totpCode = totp.ComputeTotp()
        do! page.GetByPlaceholder("XXXXXX").FillAsync(totpCode)

    }

let login (context: IBrowserContext) (page: IPage) =
    task {
        let verify2FAText = "Verify 2FA now"
        let! verify2FA = page.GetByText(verify2FAText).IsVisibleAsync()

        if verify2FA then
            do!
                page
                    .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = verify2FAText, Exact = true))
                    .ClickAsync()

            do! fill2FA page

            do!
                page
                    .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Done", Exact = true))
                    .ClickAsync()

            do! page.WaitForURLAsync("https://githubfhdjslhfjdlsfdkljs.com/")
            ()
        else

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

            do! fill2FA page
            do! page.WaitForURLAsync("https://github.com/")
            // This saves the contect to disk, what a weird api....
            let! state = context.StorageStateAsync(BrowserContextStorageStateOptions(Path = browserStatePath))
            ()

        return page
    }

// Checks if user is logged in. Changes url of the page!
let isLoggedIn (page: IPage) =
    task {
        let! _response = page.GotoAsync("https://github.com/login")
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
            |> List.map (fun r ->
                async {
                    printfn "subscribe %s/%s" r.user r.repo
                    let! _r = subscribeTo page r.user r.repo
                    let! _r = Asfaload.Collector.Queue.triggerReleaseDownload r.user r.repo |> Async.AwaitTask
                    return ()
                })
            |> Async.Sequential

        let! _r =
            unsubscribedRepos
            |> List.map (fun r -> Repos.setSubscribed r |> Repos.run)
            |> Async.Sequential

        printfn "%A Sleeping before next subscription loop" DateTime.Now
        do! Async.Sleep 30000
        return! subscriptionLoop page

    }

let main () =
    task {
        let! page, context = firefoxPage ()

        let bodyAsync =
            task {
                match! isLoggedIn (page) with
                | true -> printfn "logged in!"
                | false ->
                    printfn "logging in"
                    let! _ = login context page
                    ()

                return! subscriptionLoop page
            }
            |> Async.AwaitTask

        let! res = bodyAsync |> Async.Catch

        match res with
        | Choice1Of2 r -> return 0
        | Choice2Of2 exc ->
            // Close page and context for videos to be saved
            do! page.CloseAsync()
            do! context.CloseAsync()
            printfn "Caught exception %s:\n%s" exc.Message exc.StackTrace
            return 1
    }

main () |> Async.AwaitTask |> Async.RunSynchronously |> exit
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
//|> Request.send
