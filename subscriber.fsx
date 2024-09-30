#r "nuget: FsHttp"
#r "nuget: Microsoft.Playwright, 1.47.0"
// Getting playwright installed:
// // Create a new project ni which you install playwright
// dotnet new console -lang F#
// dotnet add package Microsoft.Playwright --version 1.47.0
// // install powershell
// mise use powershell-core
// // Then run the script installing browsers
// pwsh bin/Debug/net8.0/playwright.ps1 install

open System

open Microsoft.Playwright

let fromEnv = Environment.GetEnvironmentVariable

let subscribeTo (page: IPage) (username: string) (repo: string) =
    task {
        let! _response = page.GotoAsync($"https://github.com/{username}/{repo}")

        Console.ReadLine() |> ignore

        do!
            page
                .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Watch"))
                .ClickAsync()

        Console.ReadLine() |> ignore
        do! page.GetByText("Custom", PageGetByTextOptions(Exact = true)).ClickAsync()
        Console.ReadLine() |> ignore

        do!
            page
                .GetByRole(AriaRole.Checkbox, PageGetByRoleOptions(Name = "Releases", Exact = true))
                .CheckAsync()

        Console.ReadLine() |> ignore

        do!
            page
                .GetByRole(AriaRole.Button, PageGetByRoleOptions(Name = "Apply", Exact = true))
                .ClickAsync()

        return true

    }

let firefoxAsync () =
    task {
        let! playwright = Playwright.CreateAsync()
        let firefox = playwright.Firefox
        let! browser = firefox.LaunchAsync(BrowserTypeLaunchOptions(Headless = false))
        return browser

    }

let firefoxPage () =
    task {
        printfn "starting browser"
        let! browser = firefoxAsync ()
        printfn "setting page"
        let! page = browser.NewPageAsync()
        printfn "got page"
        return page
    }

let login (page: IPage) =
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

        Console.ReadLine() |> ignore
        return page
    }

let main () =
    task {
        let! page = firefoxPage ()
        let! _ = login page
        let! b = subscribeTo page "rbauduin" "TestRepo"
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
