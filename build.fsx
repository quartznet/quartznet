#r "packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.AssemblyInfoFile
open Fake.Git

open System
open System.Collections.Generic
open System.IO

let commitHash = Information.getCurrentHash()
let configuration = getBuildParamOrDefault "configuration" "Release"
let projectJsonFiles = !! "src/*/project.json" -- "src/*Web*/project.json"
let testProjectJsonFiles = !! "src/Quartz.Tests.Integration/project.json" ++ "src/Quartz.Tests.Unit/project.json"

Target "Clean" (fun _ ->
    !! "artifacts" ++ "src/*/bin" ++ "src/*/obj" ++ "test/*/bin" ++ "test/*/obj" ++ "build" ++ "deploy"
        |> CleanDirs
)

Target "GenerateAssemblyInfo" (fun _ ->
    CreateCSharpAssemblyInfo "./src/AssemblyInfo.cs"
        [
            (Attribute.Product("Quarz.NET"))
            (Attribute.Description("Quartz Scheduling Framework for .NET"))
            (Attribute.Copyright("Copyright 2001-2016 Marko Lahma"))
            (Attribute.Trademark("Apache License, Version 2.0"))
            (Attribute.Company("http://www.quartz-scheduler.net/"))
            (Attribute.CLSCompliant(true))
            (Attribute.ComVisible(false))
            (Attribute.Metadata("githash", commitHash))]
)

Target "Build" (fun _ ->

    let restore f = DotNetCli.Restore (fun p ->
                { p with
                    AdditionalArgs = [f] })

    let build f = DotNetCli.Build (fun p ->
                { p with
                    Configuration = configuration
                    Project = f })    

    projectJsonFiles
        |> Seq.iter restore

    projectJsonFiles
        |> Seq.iter build
)

Target "BuildSolutions" (fun _ ->

    let setParams defaults =
            { defaults with
                Verbosity = Some(Quiet)
                Targets = ["Build"]
                Properties =
                    [
                        "Optimize", "True"
                        "DebugSymbols", "True"
                        "Configuration", configuration
                    ]
            }
    build setParams "./Quartz.sln"
        |> DoNothing

    build setParams "./Quartz-DotNetCore.sln"
        |> DoNothing
)

Target "Pack" (fun _ ->

    let pack f = DotNetCli.Pack (fun p ->
                { p with
                    Configuration = "Release"
                    Project = f
                })

    !! "src/Quartz/project.json" ++ "src/Quartz.Serialization.Json/project.json"
        |> Seq.iter pack

    !! "src/*/bin/**/*.nupkg"
        |> Copy "artifacts"
)

Target "Test" (fun _ ->
        DotNetCli.Test
            (fun p ->
                { p with
                    Project = "src/Quartz.Tests.Unit/project.json"
                    Configuration = configuration
                    AdditionalArgs = ["--where \"cat != database && cat != fragile\""] })
)

Target "TestFull" (fun _ ->

    let test f = DotNetCli.Test (fun p ->
                    { p with
                        Project = f
                        Configuration = configuration
                        AdditionalArgs = ["--where \"cat != fragile\""] })

    testProjectJsonFiles
        |> Seq.iter test
)


Target "TestLinux" (fun _ ->
    let test f = DotNetCli.Test (fun p ->
                    { p with
                        Project = f
                        Configuration = configuration
                        AdditionalArgs = ["--where \"cat != fragile && cat != sqlserver && cat != windowstimezoneid\""] })
                                
    testProjectJsonFiles
        |>  Seq.iter test
)

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
  =?> ("BuildSolutions", hasBuildParam "buildSolutions")
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
