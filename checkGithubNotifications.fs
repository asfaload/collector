// Script that will look at all notifications from github.
// It loops continually, waiting for the poll interval sent by github
// to expire before the next iteration.
// It uses the Last-Modified headers to request only new notifications. When no
// change is available, a Not Modified response is returned by github, and it doesn't
// count regarding the requests quota.
// When a new release is available, it sends it on the Queue for another script to
// collect the checksums of the release.

open Asfaload.Collector
open GithubNotifications
open Rest

let releasesHandler (notificationData: Notification.NotificationData.Root) =
    task {

        let repo =
            { user = notificationData.Repository.Owner.Login
              repo = notificationData.Repository.Name
              kind = Github
              checksums = [] }

        printfn "registering release %A://%s/%s" repo.kind repo.user repo.repo
        do! Queue.publishRepoRelease repo
    }

loop releasesHandler
