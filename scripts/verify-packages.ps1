<#
.SYNOPSIS
    Builds, tests and packs Audio-Centric-Controls, then checks that the packages work on their own.

.DESCRIPTION
    1. Builds the solution (x64 Release, warnings are errors) and runs the tests.
    2. Packs AudioCentric.Controls.Core and AudioCentric.Controls.Wpf into bin\x64\Release\packages\.
    3. Checks each package for its library, XML docs, README and icon, and checks that Wpf depends on Core.
    4. Builds examples\PackageConsumer.Wpf from the packages only (no project references, a private restore folder,
       and none of the repository's build settings).
    5. Runs the consumer in smoke mode: it shows its window, checks that every control type is on screen, saves a
       screenshot and exits with 0.

    Works in Windows PowerShell 5.1 and PowerShell 7. Run it from anywhere:
        powershell -ExecutionPolicy Bypass -File scripts\verify-packages.ps1
#>
[CmdletBinding()]
param(
    [switch] $SkipTests,
    [string] $ScreenshotPath = (Join-Path ([IO.Path]::GetTempPath()) 'acc-package-consumer.png')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sln = Join-Path $root 'Audio-Centric-Controls.sln'
$packages = Join-Path $root 'bin\x64\Release\packages'
$consumer = Join-Path $root 'examples\PackageConsumer.Wpf'

function Invoke-Step([string] $title, [scriptblock] $action) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$title failed (exit code $LASTEXITCODE)." }
}

$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
Write-Host "Package version $version"

Invoke-Step 'Build (x64 Release)' { dotnet build $sln -c Release -nologo -v q }
if (-not $SkipTests) { Invoke-Step 'Test' { dotnet test $sln -c Release --no-build -nologo -v q } }

if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }
Invoke-Step 'Pack' { dotnet pack $sln -c Release --no-build -nologo -v q }

Write-Host '==> Inspect packages' -ForegroundColor Cyan
Add-Type -AssemblyName System.IO.Compression.FileSystem
$expect = @{
    'AudioCentric.Controls.Core' = @('lib/net8.0/AudioCentric.Controls.Core.dll', 'lib/net8.0/AudioCentric.Controls.Core.xml')
    'AudioCentric.Controls.Wpf'  = @('lib/net8.0-windows7.0/AudioCentric.Controls.Wpf.dll', 'lib/net8.0-windows7.0/AudioCentric.Controls.Wpf.xml')
}
foreach ($id in $expect.Keys) {
    $file = Join-Path $packages "$id.$version.nupkg"
    if (-not (Test-Path $file)) { throw "Missing package $file" }
    if (-not (Test-Path (Join-Path $packages "$id.$version.snupkg"))) { throw "Missing symbol package for $id" }
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    try {
        $entries = $zip.Entries | ForEach-Object { $_.FullName }
        foreach ($e in $expect[$id] + @('README.md', 'icon.png')) {
            if ($entries -notcontains $e) { throw "$id is missing $e" }
        }
        $nuspec = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' }
        $reader = New-Object IO.StreamReader($nuspec.Open())
        $text = $reader.ReadToEnd()
        $reader.Close()
        if ($id -eq 'AudioCentric.Controls.Wpf' -and $text -notmatch 'dependency id="AudioCentric.Controls.Core" version="' + [regex]::Escape($version)) {
            throw 'AudioCentric.Controls.Wpf does not depend on the matching AudioCentric.Controls.Core'
        }
    }
    finally { $zip.Dispose() }
    Write-Host "    $id $version OK"
}

# A clean consumer build: no stale restore, no stale output.
foreach ($dir in @((Join-Path $consumer 'obj'), (Join-Path $consumer 'bin'), (Join-Path $root 'bin\x64\Release\PackageConsumer'))) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
Invoke-Step 'Build the package consumer from the packages' {
    dotnet build (Join-Path $consumer 'PackageConsumer.Wpf.csproj') -c Release -nologo -v q "-p:AccPackageVersion=$version"
}

Write-Host '==> Smoke-test the package consumer' -ForegroundColor Cyan
$exe = Join-Path $root 'bin\x64\Release\PackageConsumer\PackageConsumer.Wpf.exe'
$report = [IO.Path]::ChangeExtension($ScreenshotPath, '.txt')
if (Test-Path $report) { Remove-Item $report }
$proc = Start-Process $exe -ArgumentList '--smoke', "`"$ScreenshotPath`"" -Wait -PassThru
if (Test-Path $report) { Get-Content $report | ForEach-Object { Write-Host "    $_" } }
if ($proc.ExitCode -ne 0) { throw "Package consumer smoke test failed (exit code $($proc.ExitCode))." }

Write-Host ''
Write-Host "All checks passed. Packages: $packages" -ForegroundColor Green
Write-Host "Consumer screenshot: $ScreenshotPath"
