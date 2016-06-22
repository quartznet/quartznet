@echo off
cd %~dp0

SETLOCAL
"tools\NuGet\NuGet.exe" "Install" "psake" "-OutputDirectory" "tools" "-ExcludeVersion"
"tools\NuGet\NuGet.exe" "Install" "NUnit.Runners" "-OutputDirectory" "tools" "-ExcludeVersion"
powershell.exe -NoProfile -ExecutionPolicy unrestricted -Command "& {Import-Module '.\tools\psake\tools\psake.psm1'; invoke-psake .\default.ps1 %*; if ($lastexitcode -ne 0) {write-host "ERROR: $lastexitcode" -fore RED; exit $lastexitcode} }" 
