#r "packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.AssemblyInfoFile
open Fake.Git

open System
open System.Collections.Generic
open System.IO

let commitHash = Information.getCurrentHash()
let configuration = getBuildParamOrDefault "configuration" "Release"

Target "Clean" (fun _ ->
    !! "artifacts" ++ "src/*/bin" ++ "src/*/obj" ++ "test/*/bin" ++ "test/*/obj" ++ "build" ++ "deploy"
        |> CleanDirs
)

Target "GenerateAssemblyInfo" (fun _ ->
    CreateCSharpAssemblyInfo "./src/AssemblyInfo.cs"
        [
            (Attribute.Metadata("githash", commitHash))]
)

Target "Build" (fun _ ->

    let buildMode = getBuildParamOrDefault "buildMode" "Release"
    let setParams defaults =
            { defaults with
                Verbosity = Some(Quiet)
                Targets = ["Restore"; "Build"]
                Properties =
                    [
                        "Optimize", "True"
                        "Configuration", buildMode
                    ]
            }
    build setParams "./Quartz.sln"
        |> DoNothing
)

Target "Pack" (fun _ ->

    let pack f = DotNetCli.Pack (fun p ->
                { p with
                    Configuration = "Release"
                    Project = f
                })

    !! "src/Quartz/Quartz.csproj" ++ "src/Quartz.Serialization.Json/Quartz.Serialization.Json.csproj"
        |> Seq.iter pack

    !! "src/*/bin/**/*.nupkg"
        |> Copy "artifacts"
)

Target "Test" (fun () ->  trace " --- Test not implemented --- ")

Target "TestFull" (fun () ->  trace " --- TestFull not implemented --- ")

Target "TestLinux" (fun () ->  trace " --- TestLinux not implemented --- ")

Target "ApiDoc" (fun _ ->

    let setParams defaults =
            { defaults with
                Verbosity = Some(Quiet)
                Targets = ["Build"]
                Properties =
                    [
                        "Configuration", "Release"
                    ]
            }
    build setParams "./Quartz.sln"
        |> DoNothing

    let setShfbParams defaults =
        { defaults with
            Verbosity = Some(Quiet)
            Targets = ["Build"]
            Properties =
                [
                    "CleanIntermediates", "True"
                    "Configuration", "Release"
                ]
        }
    build setShfbParams "doc/quartznet.shfbproj"
        |> DoNothing

    let headerContent = ReadFileAsString "doc/header.template"
    let footerContent = ReadFileAsString "doc/footer.template"

    !! "build/apidoc/**/*.htm" ++ "build/apidoc/**/*.html"
        |> ReplaceInFiles [("@HEADER@", footerContent);("@FOOTER@", headerContent)]

)

"Clean"
  ==> "GenerateAssemblyInfo"
  ==> "Build"
  ==> "Test"
  ==> "Pack"


"Clean"
  ==> "GenerateAssemblyInfo"
  ==> "ApiDoc"

"Clean"
  ==> "GenerateAssemblyInfo"
  ==> "TestFull"


"Clean"
  ==> "GenerateAssemblyInfo"
  ==> "Build"
  ==> "TestLinux"

RunTargetOrDefault "Test"
