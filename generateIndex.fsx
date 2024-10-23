open System.IO

let rec getLeafDirectories (root: string) =
    seq {
        // ignore hidden directories like eg .git
        if DirectoryInfo(root).Name.StartsWith "." then
            ()
        else
            let subDirs = Directory.EnumerateDirectories root

            match subDirs |> Seq.length with
            // If the root has no subdir, it is a leaf, so return it
            | 0 -> yield root
            // If the root has subdirs, recursively get leafs from them
            | _ ->
                for subDir in subDirs do
                    for leaf in (getLeafDirectories subDir) do
                        yield leaf
    }

let main () =
    let gitDir = System.Environment.GetEnvironmentVariable("BASE_DIR")
    getLeafDirectories gitDir |> Seq.iter (fun leaf -> printfn "%s" leaf)


main ()
