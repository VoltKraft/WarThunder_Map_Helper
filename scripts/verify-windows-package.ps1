<#
.SYNOPSIS
Extracts the native MSI without installing it, compares its payload, and smoke-tests it.
.DESCRIPTION
Requires a matching native Windows host, a completed packaging build, and Python
3.11+. Keeps logs, the isolated profile, and screenshot under artifacts. Extracted
files remain in a unique system temporary directory, recorded in the report.
Both MSI extraction and application startup have a two-minute timeout.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Version,
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $repoVersion = & $Python scripts/release_metadata.py --format version
    if ($LASTEXITCODE -ne 0) { throw 'Release metadata validation failed.' }
    if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $repoVersion.Trim() }
    if ($Version -cne $repoVersion.Trim()) { throw 'Version must match project metadata.' }
    $native = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    if ($Runtime -cne "win-$native") { throw 'The MSI must be smoke-tested on its native architecture.' }
    & $Python scripts/verify-publish.py $Version --runtime $Runtime
    if ($LASTEXITCODE -ne 0) { throw 'Publish payload verification failed.' }

    $publish = Join-Path $root "artifacts/publish/$Runtime-$Version"
    $msi = Join-Path $root "artifacts/release/WarThunderMapHelper-$Version-$Runtime.msi"
    $work = Join-Path $root "artifacts/msi-smoke/$Runtime-$Version-$([Guid]::NewGuid().ToString('N'))"
    # Windows Installer still applies MAX_PATH to nested license files. A short
    # temporary root also works when the repository is under a long synced path.
    $extract = Join-Path ([IO.Path]::GetTempPath()) "wtmh-$([Guid]::NewGuid().ToString('N'))"
    [void](New-Item -ItemType Directory -Path $work, $extract)
    $log = Join-Path $work 'extraction.log'
    $process = Start-Process -FilePath msiexec.exe -ArgumentList '/a',"`"$msi`"",'/qn',"TARGETDIR=`"$extract`"",'/L*v',"`"$log`"" -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "MSI extraction timed out. See $log" }
    if ($process.ExitCode -ne 0) { throw "MSI extraction failed ($($process.ExitCode)). See $log" }
    $apps = @(Get-ChildItem -LiteralPath $extract -Recurse -File -Filter WarThunderMapHelper.exe)
    if ($apps.Count -ne 1) { throw 'Expected exactly one application in the extracted MSI.' }
    $appRoot = $apps[0].Directory.FullName
    $expected = @(Get-ChildItem -LiteralPath $publish -Recurse -File | ForEach-Object { $_.FullName.Substring($publish.Length + 1) })
    $actual = @(Get-ChildItem -LiteralPath $appRoot -Recurse -File | ForEach-Object { $_.FullName.Substring($appRoot.Length + 1) })
    if (@(Compare-Object $expected $actual -CaseSensitive).Count -ne 0) { throw 'MSI payload file list differs from publish output.' }
    foreach ($relative in $expected) {
        $sourceHash = (Get-FileHash -LiteralPath (Join-Path $publish $relative) -Algorithm SHA256).Hash
        $packageHash = (Get-FileHash -LiteralPath (Join-Path $appRoot $relative) -Algorithm SHA256).Hash
        if ($sourceHash -cne $packageHash) { throw "MSI payload differs from publish output: $relative" }
    }
    [void](New-Item -ItemType Directory -Path artifacts/screenshots -Force)
    $screenshot = Join-Path $root "artifacts/screenshots/$Runtime-msi-$Version.png"
    if (Test-Path -LiteralPath $screenshot) { Remove-Item -LiteralPath $screenshot -Force }
    $profile = Join-Path $work 'profile'
    $process = Start-Process -FilePath $apps[0].FullName -ArgumentList '--demo','--smoke-test',"`"--screenshot=$screenshot`"","`"--data-dir=$profile`"" -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Windows MSI smoke test timed out.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $screenshot) -or (Get-Item -LiteralPath $screenshot).Length -eq 0) {
        throw "Windows MSI smoke test failed. See $profile"
    }
    @{ runtime = $Runtime; files = $expected.Count; allFilesMatchPublish = $true; smokeTestPassed = $true; extractedPath = $extract } |
        ConvertTo-Json | Set-Content -LiteralPath "artifacts/msi-smoke-verification-$Runtime.json" -Encoding utf8
    Write-Output "MSI extracted, verified, and started successfully: $work"
} finally { Pop-Location }
