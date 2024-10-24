#r "nuget: FSharp.SystemTextJson, 1.3.13"

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

open System.Text.Json.Serialization

type Algo =
    | Sha256
    | Sha512

type FileChecksum =
    { fileName: string
      algo: Algo
      source: string
      hash: string }

type FilesChecksums = seq<FileChecksum>

type IndexFile =
    { mirroredOn: DateTime option
      publishedOn: DateTime option
      publishedFiles: FilesChecksums }

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

let handleChecksumFile (path: string) : FilesChecksums =
    File.ReadLines path
    |> Seq.map (fun line -> line.Split(" ") |> Array.filter (fun e -> e <> ""))
    |> Seq.map (fun parts ->

        let checksum, file =
            match parts with
            | [| sha; fileName |] ->
                if fileName.StartsWith("*") then
                    let regex = Regex(Regex.Escape("*"))
                    (sha, regex.Replace(fileName, "", 1).Trim())
                else
                    (sha, fileName.Trim())
            | other ->
                printfn "%A" other
                failwithf "error handling line parts\n%A\n from file %s" parts path

        let algo =
            match checksum.Length with
            | 64 -> Sha256
            | 128 -> Sha512
            | _ -> failwithf "In file %s,\n unknown checksum algo for checksum value:\n%s" path checksum

        { fileName = file
          algo = algo
          source = (FileInfo(path).Name)
          hash = checksum }


    )


let handleChecksumsFilesInLeaf (leafDir: string) =
    let checksums =
        Directory.EnumerateFiles leafDir
        // Filter out PGP signature files
        |> Seq.filter (fun f ->
            let firstLine = File.ReadLines f |> Seq.head
            firstLine <> "-----BEGIN PGP SIGNED MESSAGE-----")
        |> Seq.map (fun checksumFile -> handleChecksumFile checksumFile)
        // Merge all infor from all checksums files in one Seq
        |> Seq.concat

    { mirroredOn = None
      publishedOn = None
      publishedFiles = checksums }

let generateChecksumsList (rootDir: string) =
    getLeafDirectories rootDir
    |> Seq.map (fun leafDir ->
        let checksumsInfo = handleChecksumsFilesInLeaf leafDir

        let options =
            JsonFSharpOptions
                .Default()
                // DU cases without fields are serialised as string,
                // eg Sha256 -> "Sha256"
                .WithUnionUnwrapFieldlessTags()
                .ToJsonSerializerOptions()

        let json = JsonSerializer.Serialize(checksumsInfo, options)
        let indexPath = Path.Combine(leafDir, ".asfaload.index.json")
        File.WriteAllText(indexPath, json)
        printfn "wrote %s" indexPath)
    |> Seq.iter (fun _ -> ())


let main () =
    let gitDir = System.Environment.GetEnvironmentVariable("BASE_DIR")
    //getLeafDirectories gitDir |> Seq.iter (fun leaf -> printfn "%s" leaf)
    generateChecksumsList gitDir


main ()
