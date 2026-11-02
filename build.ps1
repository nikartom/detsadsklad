# Собирает программу в один файл dist\DetsadSklad.exe.
# Нужен MSBuild (Visual Studio или Build Tools); .NET SDK не требуется.
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$refPack = Join-Path $root ".tools\refs45"

if (-not (Test-Path (Join-Path $refPack "build\.NETFramework\v4.5"))) {
    Write-Host "Скачиваю справочные сборки .NET 4.5 с nuget.org..."
    New-Item -ItemType Directory -Force (Join-Path $root ".tools") | Out-Null
    $zip = Join-Path $root ".tools\refs45.zip"
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net45/1.0.3/microsoft.netframework.referenceassemblies.net45.1.0.3.nupkg" -OutFile $zip
    Expand-Archive $zip $refPack -Force
    Remove-Item $zip
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild не найден. Установите Visual Studio или Build Tools." }

& $msbuild (Join-Path $root "tests\DetsadSklad.Tests\DetsadSklad.Tests.csproj") /t:Rebuild /p:Configuration=$Configuration /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась." }

& (Join-Path $root "tests\DetsadSklad.Tests\bin\$Configuration\DetsadSklad.Tests.exe")
if ($LASTEXITCODE -ne 0) { throw "Проверки не прошли." }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $root "src\DetsadSklad\bin\$Configuration\DetsadSklad.exe") $dist -Force
Write-Host "Готово: $dist\DetsadSklad.exe"
