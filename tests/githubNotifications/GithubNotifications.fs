module tests.GithubNotifications

open NUnit.Framework
open FsUnit

[<SetUp>]
let Setup () = ()

[<Test>]
let Test1 () = true |> should equal true
