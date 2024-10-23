open System
open System.IO
open System.Text.RegularExpressions

type Algo =
    | Sha256
    | Sha512

type Checksum =
    { algo: Algo
      source: string
      value: string }

type fileChecksum = Map<string, Checksum>
type filesChecksums = seq<fileChecksum>

type IndexFile =
    { mirroredOn: DateTime
      publishedOn: DateTime
      publishedFiles: filesChecksums }

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

let handleChecksumFile (path: string) : seq<fileChecksum> =
    File.ReadLines path
    |> Seq.map (fun line ->
        let parts = line.Split(" ")

        let checksum, file =
            match parts with
            | [| sha; fileName |] ->
                if fileName.StartsWith("*") then
                    let regex = Regex(Regex.Escape("*"))
                    (sha, regex.Replace(fileName, "", 1).Trim())
                else
                    (sha, fileName.Trim())
            | _ -> failwithf "error handling line\n%s\n from file %s" line path

        let algo =
            match checksum.Length with
            | 64 -> Sha256
            | 128 -> Sha512
            | _ -> failwithf "In file %s,\n unknown checksum algo for checksum value:\n%s" path checksum

        let checksumRecord =
            { algo = algo
              source = FileInfo(path).Name
              value = checksum }

        Map.empty.Add(file, checksumRecord)

    )


let handleChecksumsFilesInLeaf (leafDir: string) =
    Directory.EnumerateFiles leafDir
    |> Seq.map (fun checksumFile -> handleChecksumFile checksumFile)

let generateChecksumsList (rootDir: string) =
    getLeafDirectories rootDir
    |> Seq.map (fun leafDir -> handleChecksumsFilesInLeaf leafDir


    )


let main () =
    let gitDir = System.Environment.GetEnvironmentVariable("BASE_DIR")
    getLeafDirectories gitDir |> Seq.iter (fun leaf -> printfn "%s" leaf)


main ()
main ()
