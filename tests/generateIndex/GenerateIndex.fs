module tests.GenerateIndex

open Asfaload.Collector.Index
open NUnit.Framework
open FsUnit


open System.IO


[<SetUp>]
let Setup () = ()

[<OneTimeSetUp>]
let OneTimeSetup () =
    System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener())
    |> ignore

[<OneTimeTearDown>]
let OneTimeTearDown () = System.Diagnostics.Trace.Flush()


[<Test>]
let test_getLeafDirectories () =
    let r = getLeafDirectories "./fixtures/getLeafDirectories"

    r
    |> should
        equal
        [| "./fixtures/getLeafDirectories/src/js"
           "./fixtures/getLeafDirectories/src/fsharp/lib" |]

[<Test>]
let test_filterLines () =
    // ignore comment lines
    "# this is a comment line" |> filterLines |> should equal false
    // ignore empty lines
    "" |> filterLines |> should equal false
    // sha256 line
    "3cb16133432cffbd2433a31a41a65fa4b6ab58ad527d5c6a9cfe8c093a4306fd  myfile"
    |> filterLines
    |> should equal true
    // Non-empty line
    "blabla" |> filterLines |> should equal true

[<Test>]
let test_hasHashLength () =
    let md5 = "18cfccc69ddb9947c9abedf87be3127f"
    let sha1 = "a66ba8f482820cc0b4f0e43396f94692ba4d636c"
    let sha256 = "3cb16133432cffbd2433a31a41a65fa4b6ab58ad527d5c6a9cfe8c093a4306fd"

    let sha512 =
        "39a8a7d9c2c3450e39a8161ac6386c6233b92e40725c0ef1efd4b295110a30258d0cf4442c1eb8b7f1d042948714858327ef12ddde66e923275f66f2725f7652"
    // ignore comment lines

    [| md5; sha1; sha256; sha512 |]
    |> Array.iter (fun h -> h |> hasHashLength |> should equal true)

    "other length string" |> hasHashLength |> should equal false

[<Test>]
let test_handleChecksumsFile () =
    let r = handleChecksumFile "./fixtures/handleChecksumsFile/combined.txt"

    // sha256 and 512 in same file, separated by a comment line
    r
    |> should
        equal
        [| { fileName = "man.local"
             algo = Sha256
             source = "combined.txt"
             hash = "600a366f00ab57d469745d05b89f6976d68a29dd1b39196f4b86340b66224b31" }
           { fileName = "mdoc.local"
             algo = Sha256
             source = "combined.txt"
             hash = "da4f2bd8b36d5469598d93c08fe2c87f46868d123ab85f8d1c5a8f686f3666b9" }
           { fileName = "man.local"
             algo = Sha512
             source = "combined.txt"
             hash =
               "9a77d4f1b799b75a1b7e1c78b47393ef8b7f1107a1d8a8ab903f2b95150b756e54bd1fcde87d4c48dbfda5b6db28d32974123a7bddd73b07bf75c0c48e9f4854" }
           { fileName = "mdoc.local"
             algo = Sha512
             source = "combined.txt"
             hash =
               "4ac54acf77ee3ac6aef0585c601775dcc7edcd46184535032ea61b20cf2068d7e09a727b7a00ad02d8273dec9e5366e09c995b16e5058870ea997cc7640dc910" } |]


    // standard sha256sum
    let r =
        handleChecksumFile "./fixtures/handleChecksumsFile/checksums_256_standard.txt"

    r
    |> should
        equal
        [| { fileName = "man.local"
             algo = Sha256
             source = "checksums_256_standard.txt"
             hash = "600a366f00ab57d469745d05b89f6976d68a29dd1b39196f4b86340b66224b31" }
           { fileName = "mdoc.local"
             algo = Sha256
             source = "checksums_256_standard.txt"
             hash = "da4f2bd8b36d5469598d93c08fe2c87f46868d123ab85f8d1c5a8f686f3666b9" } |]


    // standard sha512sum
    let r =
        handleChecksumFile "./fixtures/handleChecksumsFile/checksums_512_standard.txt"

    r
    |> should
        equal
        [| { fileName = "man.local"
             algo = Sha512
             source = "checksums_512_standard.txt"
             hash =
               "9a77d4f1b799b75a1b7e1c78b47393ef8b7f1107a1d8a8ab903f2b95150b756e54bd1fcde87d4c48dbfda5b6db28d32974123a7bddd73b07bf75c0c48e9f4854" }
           { fileName = "mdoc.local"
             algo = Sha512
             source = "checksums_512_standard.txt"
             hash =
               "4ac54acf77ee3ac6aef0585c601775dcc7edcd46184535032ea61b20cf2068d7e09a727b7a00ad02d8273dec9e5366e09c995b16e5058870ea997cc7640dc910" } |]

    let r = handleChecksumFile "./fixtures/handleChecksumsFile/man.local.sha256"

    // sha256 in same filename with suffix
    r
    |> should
        equal
        [| { fileName = "man.local"
             algo = Sha256
             source = "man.local.sha256"
             hash = "600a366f00ab57d469745d05b89f6976d68a29dd1b39196f4b86340b66224b31" }

           |]

    // sha512 in same filename with suffix
    let r = handleChecksumFile "./fixtures/handleChecksumsFile/man.local.sha512"

    r
    |> should
        equal
        [| { fileName = "man.local"
             algo = Sha512
             source = "man.local.sha512"
             hash =
               "9a77d4f1b799b75a1b7e1c78b47393ef8b7f1107a1d8a8ab903f2b95150b756e54bd1fcde87d4c48dbfda5b6db28d32974123a7bddd73b07bf75c0c48e9f4854" } |]

    // md5sum
    let r = handleChecksumFile "./fixtures/handleChecksumsFile/checksums_md5.txt"

    r
    |> should
        equal
        [| { fileName = "man.local"
             algo = Md5
             source = "checksums_md5.txt"
             hash = "e6591616404c7c443f71ff21d27430d7" }
           { fileName = "mdoc.local"
             algo = Md5
             source = "checksums_md5.txt"
             hash = "4bc6267468942826b757fa2f868c8237" }

           |]

    // sha1sum
    let r = handleChecksumFile "./fixtures/handleChecksumsFile/checksums_sha1.txt"

    r
    |> should
        equal
        [| { fileName = "man.local"
             algo = Sha1
             source = "checksums_sha1.txt"
             hash = "e65ef2997ff54bbb09bd4a2070bb75cd94aba538" }
           { fileName = "mdoc.local"
             algo = Sha1
             source = "checksums_sha1.txt"
             hash = "c5348c321bcbede39828346bdb2f48f87956ebc2" }

           |]

[<Test>]
let test_handleChecksumsInLeaf () =
    let publishedOn = Some <| System.DateTimeOffset.Now.AddDays(-1)
    let mirroredOn = Some <| System.DateTimeOffset.Now.AddMinutes(-5)

    let publishedFiles =
        [| { fileName = "man.local"
             algo = Sha512
             source = "checksums_512_standard.txt"
             hash =
               "9a77d4f1b799b75a1b7e1c78b47393ef8b7f1107a1d8a8ab903f2b95150b756e54bd1fcde87d4c48dbfda5b6db28d32974123a7bddd73b07bf75c0c48e9f4854" }
           { fileName = "mdoc.local"
             algo = Sha512
             source = "checksums_512_standard.txt"
             hash =
               "4ac54acf77ee3ac6aef0585c601775dcc7edcd46184535032ea61b20cf2068d7e09a727b7a00ad02d8273dec9e5366e09c995b16e5058870ea997cc7640dc910" }
           { fileName = "man.local"
             algo = Sha256
             source = "checksums_256_standard.txt"
             hash = "600a366f00ab57d469745d05b89f6976d68a29dd1b39196f4b86340b66224b31" }
           { fileName = "mdoc.local"
             algo = Sha256
             source = "checksums_256_standard.txt"
             hash = "da4f2bd8b36d5469598d93c08fe2c87f46868d123ab85f8d1c5a8f686f3666b9" } |]

    let r =
        handleChecksumsFilesInLeaf
            "./fixtures/handleChecksumsInLeaf/asfaload/asfald/release/v0.0.1/"
            publishedOn
            mirroredOn

    r
    |> should
        equal
        { mirroredOn = mirroredOn
          publishedOn = publishedOn
          version = 1
          publishedFiles = publishedFiles }

    // Without published or mirrored on info
    let r =
        handleChecksumsFilesInLeaf "./fixtures/handleChecksumsInLeaf/asfaload/asfald/release/v0.0.1/" None None

    r
    |> should
        equal
        { mirroredOn = None
          publishedOn = None
          version = 1
          publishedFiles = publishedFiles }


    // Tests with multiple files each holding one checksum
    // ---------------------------------------------------

    let r =
        handleChecksumsFilesInLeaf
            "./fixtures/handleChecksumsInLeaf/charles/chaplet/release/v2.0.1/"
            publishedOn
            mirroredOn

    r
    |> should
        equal
        { mirroredOn = mirroredOn
          publishedOn = publishedOn
          version = 1
          publishedFiles =
            [| { fileName = "mdoc.local"
                 algo = Sha256
                 source = "mdoc.local.sha256"
                 hash = "da4f2bd8b36d5469598d93c08fe2c87f46868d123ab85f8d1c5a8f686f3666b9" }
               { fileName = "mdoc.local"
                 algo = Sha512
                 source = "mdoc.local.sha512"
                 hash =
                   "4ac54acf77ee3ac6aef0585c601775dcc7edcd46184535032ea61b20cf2068d7e09a727b7a00ad02d8273dec9e5366e09c995b16e5058870ea997cc7640dc910" }
               { fileName = "man.local"
                 algo = Sha512
                 source = "man.local.sha512"
                 hash =
                   "9a77d4f1b799b75a1b7e1c78b47393ef8b7f1107a1d8a8ab903f2b95150b756e54bd1fcde87d4c48dbfda5b6db28d32974123a7bddd73b07bf75c0c48e9f4854" }
               { fileName = "man.local"
                 algo = Sha256
                 source = "man.local.sha256"
                 hash = "600a366f00ab57d469745d05b89f6976d68a29dd1b39196f4b86340b66224b31" } |] }


[<Test>]
let test_generateChecksumsList () =
    let publishedOn = Some <| System.DateTimeOffset.Parse("2025-01-23 13:45:43+00")
    let mirroredOn = Some <| System.DateTimeOffset.Parse("2025-01-25 09:12:04+00")

    let expectedAsfaloadReleaseIndexContent =
        """{"mirroredOn":"2025-01-25T09:12:04+00:00","publishedOn":"2025-01-23T13:45:43+00:00","version":1,"publishedFiles":[{"fileName":"man.local","algo":"Sha512","source":"checksums_512_standard.txt","hash":"9a77d4f1b799b75a1b7e1c78b47393ef8b7f1107a1d8a8ab903f2b95150b756e54bd1fcde87d4c48dbfda5b6db28d32974123a7bddd73b07bf75c0c48e9f4854"},{"fileName":"mdoc.local","algo":"Sha512","source":"checksums_512_standard.txt","hash":"4ac54acf77ee3ac6aef0585c601775dcc7edcd46184535032ea61b20cf2068d7e09a727b7a00ad02d8273dec9e5366e09c995b16e5058870ea997cc7640dc910"},{"fileName":"man.local","algo":"Sha256","source":"checksums_256_standard.txt","hash":"600a366f00ab57d469745d05b89f6976d68a29dd1b39196f4b86340b66224b31"},{"fileName":"mdoc.local","algo":"Sha256","source":"checksums_256_standard.txt","hash":"da4f2bd8b36d5469598d93c08fe2c87f46868d123ab85f8d1c5a8f686f3666b9"}]}"""

    let expectedCharlesReleaseIndexContent =
        """{"mirroredOn":"2025-02-11T12:08:46.8976307+01:00","publishedOn":"2025-02-09T12:15:46.8975387+01:00","version":1,"publishedFiles":[{"fileName":"mdoc.local","algo":"Sha256","source":"mdoc.local.sha256","hash":"da4f2bd8b36d5469598d93c08fe2c87f46868d123ab85f8d1c5a8f686f3666b9"},{"fileName":"mdoc.local","algo":"Sha512","source":"mdoc.local.sha512","hash":"4ac54acf77ee3ac6aef0585c601775dcc7edcd46184535032ea61b20cf2068d7e09a727b7a00ad02d8273dec9e5366e09c995b16e5058870ea997cc7640dc910"},{"fileName":"man.local","algo":"Sha512","source":"man.local.sha512","hash":"9a77d4f1b799b75a1b7e1c78b47393ef8b7f1107a1d8a8ab903f2b95150b756e54bd1fcde87d4c48dbfda5b6db28d32974123a7bddd73b07bf75c0c48e9f4854"},{"fileName":"man.local","algo":"Sha256","source":"man.local.sha256","hash":"600a366f00ab57d469745d05b89f6976d68a29dd1b39196f4b86340b66224b31"}]}"""

    let expectedExistingIndexContent =
        """{"mirroredOn":null,"publishedOn":null,"version":1,"publishedFiles":[{"fileName":"mdoc.local","algo":"Sha256","source":"mdoc.local.sha256","hash":"da4f2bd8b36d5469598d93c08fe2c87f46868d123ab85f8d1c5a8f686f3666b9"}]}"""

    generateChecksumsList "./fixtures/generateChecksumsList" publishedOn mirroredOn

    File.ReadAllText("fixtures/generateChecksumsList/asfaload/asfald/release/v0.0.1/asfaload.index.json")
    |> should equal expectedAsfaloadReleaseIndexContent

    File.ReadAllText("./fixtures/generateChecksumsList/charles/chaplet/release/v2.0.1/asfaload.index.json")
    |> should equal expectedCharlesReleaseIndexContent

    // Check an existing index is not touched
    File.ReadAllText("./fixtures/generateChecksumsList/existing_index/asfaload.index.json")
    |> should equal expectedExistingIndexContent
