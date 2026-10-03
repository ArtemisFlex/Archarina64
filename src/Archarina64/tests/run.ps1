param([string]$RomPath, [switch]$UiSmoke)
$ErrorActionPreference = 'Stop'
$archRoot = Split-Path $PSScriptRoot
$archVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$archMsbuild = & $archVswhere -latest -products '*' -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $archMsbuild) { throw 'Visual Studio MSBuild was not found.' }
Push-Location $archRoot
try {
    & $archMsbuild Archarina64.sln /p:Configuration=Debug /p:Platform=x64 /v:quiet /nologo /clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $archCsc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $archCsc /nologo /out:bin\x64\Debug\SceneBuilderTests.exe /reference:bin\x64\Debug\Archarina64.exe /reference:bin\x64\Debug\SharpOcarina.Shared.dll /reference:bin\x64\Debug\OpenTK.dll /reference:System.Windows.Forms.dll /reference:System.Xml.Linq.dll tests\SceneBuilderTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    if ($RomPath) { & .\bin\x64\Debug\SceneBuilderTests.exe $RomPath }
    else { & .\bin\x64\Debug\SceneBuilderTests.exe }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($UiSmoke) {
        & .\bin\x64\Debug\SceneBuilderTests.exe --ui-smoke
        if ($LASTEXITCODE -ne 0) { throw 'UI smoke test failed.' }
    }
}
finally { Pop-Location }
