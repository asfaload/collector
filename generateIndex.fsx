#r "nuget: FSharp.SystemTextJson, 1.3.13"
// TODO::
// - support sha256 files from Deno for windows release
// - support inverted columns order, like termux/termux-packages
open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions

open System.Text.Json.Serialization

type Algo =
    | Md5
    | Sha1
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

// Helper to ignore unrecognised or useless lines
let filterLines (l: string) =
    // There are some devs combining multiple algo in one file, indicating which algo is used with a comment
    // We ignore these here
    not (l.StartsWith "#") && l <> ""

let hasHashLength (s: string) =
    List.contains (s.Length) [ 32; 40; 64; 128 ]

let handleChecksumFile (path: string) : FilesChecksums =
    File.ReadLines path
    |> Seq.filter filterLines
    // Found files where a tab was used as separator
    |> Seq.map (fun line -> line.Replace("	", "  "))
    |> Seq.map (fun line -> line.Split(" ") |> Array.filter (fun e -> e <> ""))
    // Report lines with more than 2 parts
    |> Seq.filter (fun parts ->
        match parts |> Array.length with
        | 2 -> true
        | 1 when hasHashLength (parts[0]) -> true
        | _ ->
            printfn "incorrect format %A in %s" parts path
            false)
    |> Seq.map (fun parts ->

        let checksumAndFileOption =
            match parts with
            | [| sha; fileName |] ->
                if fileName.StartsWith("*") then
                    let regex = Regex(Regex.Escape("*"))
                    Some(sha, regex.Replace(fileName, "", 1).Trim())
                else
                    Some(sha, fileName.Trim())
            // This handles the case of the checksum being placed in a file named similarly to the
            // published file, but with an extension
            | [| sha |] when
                // FIXME: add .checksum.txt extension
                FileInfo(path).Extension.StartsWith ".sha"
                && (FileInfo(path).Extension.EndsWith "sum"
                    || FileInfo(path).Extension.EndsWith "256"
                    || FileInfo(path).Extension.EndsWith "512")
                ->
                let extension = FileInfo(path).Extension
                Some(sha, path.Substring(0, path.LastIndexOf(extension)))
            | a ->
                printfn "Impossible to infer filename: %A in file %s" a path

                printfn
                    "starts with sha %b, ends with sum: %b, ends with 256: %b, ends with 512: %b"
                    (FileInfo(path).Extension.StartsWith ".sha")
                    (FileInfo(path).Extension.EndsWith "sum")
                    (FileInfo(path).Extension.EndsWith "256")
                    (FileInfo(path).Extension.EndsWith "512")

                None


        match checksumAndFileOption with
        | None -> None
        | Some(checksum, file) ->
            let algoOption =
                match checksum.Length with
                | 32 -> Some Md5
                | 40 -> Some Sha1
                | 64 -> Some Sha256
                | 128 -> Some Sha512
                | _ ->
                    printfn "In file %s,\n unknown checksum algo for checksum value:\n%s" path checksum
                    None

            match algoOption with
            | None -> None
            | Some algo ->
                Some
                    { fileName = file
                      algo = algo
                      source = (FileInfo(path).Name)
                      hash = checksum }


    )
    // Keep only recognised algos
    |> Seq.filter Option.isSome
    // Remove the option wrapping, which all are Some
    |> Seq.map (fun o -> o.Value)


let handleChecksumsFilesInLeaf (leafDir: string) =
    let checksums =
        Directory.EnumerateFiles leafDir
        // Ignore files with usual extensions indicating it is not a checksums file
        |> Seq.filter (fun f -> not (List.contains (FileInfo(f).Extension) [ ".sig"; ".pem"; ".crt"; ".cert" ]))
        // Filter out PGP signature files
        |> Seq.filter (fun f ->
            let firstLine = File.ReadLines f |> Seq.head

            firstLine <> "-----BEGIN PGP SIGNED MESSAGE-----"
            && firstLine <> "-----BEGIN PGP SIGNATURE-----")
        // FIXME: we read all file here to re-read it later
        |> Seq.filter (fun f ->
            match
                f
                |> File.ReadLines
                |> Seq.filter filterLines
                |> Seq.exists (fun line ->
                    let partsNumber = line.Split(" ") |> Array.filter (fun e -> e <> "") |> Array.length
                    partsNumber < 1 || partsNumber > 2)
            with
            | true ->
                printfn "Incorrect format for file %s" f
                false
            | false -> true)
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
        File.WriteAllText(indexPath, json))
    |> Seq.iter (fun _ -> ())


let main gitDir = generateChecksumsList gitDir

let args = fsi.CommandLineArgs

let directory =
    if args |> Seq.length < 2 then
        System.Environment.GetEnvironmentVariable("BASE_DIR")
    else
        args[1]

main directory
