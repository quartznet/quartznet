@echo off
cls

"tools\NuGet\NuGet.exe" "Install" "FAKE" "-Version" "4.64.4" "-OutputDirectory" "packages" "-ExcludeVersion"
"packages\FAKE\tools\Fake.exe" build.fsx %*
