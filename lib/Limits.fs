namespace Asfaload.Collector

module Limits =
    type ReleasesLimits =
        { day: int option
          week: int option
          month: int option }

    type Limits = { releases: ReleasesLimits }

module User =
    open Limits

    type Profile =
        | OpenSource

        member self.limits() =
            self
            |> function
                | OpenSource ->
                    { releases =
                        { day = Some 3
                          week = Some 3
                          month = Some 3 } }

    type UserProfile = { user: string; profile: Profile }
