module FromEnv

open System
let GH_USER_AGENT = Environment.GetEnvironmentVariable("GH_USER_AGENT")
