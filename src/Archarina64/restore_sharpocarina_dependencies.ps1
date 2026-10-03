$ErrorActionPreference = "Stop"

$nuget = Join-Path $env:TEMP "nuget.exe"
if (-not (Test-Path $nuget)) {
    Invoke-WebRequest "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $nuget
}

& $nuget restore (Join-Path $PSScriptRoot "Archarina64.sln") -NonInteractive

$archive = Join-Path $env:TEMP "SharpOcarina1.67.zip"
$extract = Join-Path $env:TEMP "SharpOcarina-1.67"
Invoke-WebRequest "https://github.com/Nokaubure/SharpOcarina/releases/download/v1.67/SharpOcarina1.67.zip" -OutFile $archive
Expand-Archive $archive $extract -Force
Copy-Item (Join-Path $extract "ZeldaMsgPreview.dll") (Join-Path $PSScriptRoot "ZeldaMsgPreview.dll") -Force

$ndecRoot = Join-Path $PSScriptRoot "ndec"
New-Item -ItemType Directory -Path $ndecRoot -Force | Out-Null
foreach ($tool in @("beat.exe", "ndec.exe", "tga2png.exe", "z64decompress.exe")) {
    Copy-Item (Join-Path $extract ("ndec\" + $tool)) (Join-Path $ndecRoot $tool) -Force
}

Write-Host "SharpOcarina dependencies restored, including ndec tools. Build Archarina64.sln with Visual Studio or MSBuild."
