#r "packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.AssemblyInfoFile
open Fake.Git

let commitHash = Information.getCurrentHash()
let configuration = getBuildParamOrDefault "configuration" "Release"

Target "Clean" (fun _ ->
    !! "artifacts" ++ "package" ++ "src/*/bin" ++ "src/*/obj" ++ "test/*/bin" ++ "test/*/obj" ++ "build" ++ "deploy"
        |> CleanDirs
)

Target "GenerateAssemblyInfo" (fun _ ->
    CreateCSharpAssemblyInfo "./src/AssemblyInfo.cs"
        [
            (Attribute.Metadata("githash", commitHash))]
)

Target "Build" (fun _ ->

    DotNetCli.Build
        (fun p ->
            { p with
                 Configuration = configuration
                 Project = "Quartz.sln" })
        |> DoNothing
)

Target "Pack" (fun _ ->

    let pack f = DotNetCli.Pack (fun p ->
                { p with
                    Configuration = "Release"
                    Project = f
                })

    !! "src/Quartz/Quartz.csproj"
        ++ "src/Quartz.Jobs/Quartz.Jobs.csproj"
        ++ "src/Quartz.Plugins/Quartz.Plugins.csproj"
        ++ "src/Quartz.Serialization.Json/Quartz.Serialization.Json.csproj"
        |> Seq.iter pack

    !! "build/Release/**/*.nupkg"
        |> Copy "artifacts"

)

Target "Zip" (fun _ ->

    [ 
    !! "src/**/*"
        ++ "database/**/*"
        ++ "changelog.md"
        ++ "license.txt"
        ++ "README.md"
        ++ "*.sln"
        ++ "build.*"
        ++ "quartz.net.snk"
        ++ "build/Release/Quartz*/**/*"
        -- "src/Quartz.Benchmark/**"
        -- "src/Quartz.Web/**"
        -- "src/AssemblyInfo.cs"
        -- "build/Release/Quartz.Benchmark/**"
        -- "build/Release/Quartz.Test*/**"
        -- "build/Release/Quartz.Web/**"
        -- "**/*.nupkg"
        -- "**/*.suo"
        -- "**/*.user"
        -- "**/obj/**"
    ]
        |> CopyWithSubfoldersTo "./package/"

    Rename "./package/bin" "./package/build"

    !! ("package/**/*.*") 
       |> Zip "package" (sprintf @"./artifacts/Quartz.NET-%s.zip" buildVersion)

)

Target "Test" (fun () ->  trace " --- Test not implemented --- ")

Target "TestFull" (fun () ->  trace " --- TestFull not implemented --- ")

Target "TestLinux" (fun () ->  trace " --- TestLinux not implemented --- ")

Target "ApiDoc" (fun _ ->

    let headerContent = ReadFileAsString "doc/header.template"
    let footerContent = ReadFileAsString "doc/footer.template"

    !! "build/apidoc/**/*.htm" ++ "build/apidoc/**/*.html"
        |> ReplaceInFiles [("@HEADER@", footerContent);("@FOOTER@", headerContent)]

)

"Clean"
  ==> "GenerateAssemblyInfo"
  ==> "Build"
  ==> "Test"
  ==> "Zip"
  ==> "Pack"


"Clean"
  ==> "GenerateAssemblyInfo"
  ==> "TestFull"


"Clean"
  ==> "GenerateAssemblyInfo"
  ==> "Build"
  ==> "TestLinux"

RunTargetOrDefault "Test"
