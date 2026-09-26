[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Version,
    [string]$Python = 'python',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $projectRoot
try {
    $repoVersion = & $Python scripts/release_metadata.py --format version
    if ($LASTEXITCODE -ne 0) { throw 'Release metadata validation failed.' }
    if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $repoVersion.Trim() }
    if ($Version -cne $repoVersion.Trim()) { throw 'Version must match Directory.Build.props and CHANGELOG.md.' }
    $release = Join-Path $projectRoot 'artifacts\release'
    $publishRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\publish'))
    $publish = [IO.Path]::GetFullPath((Join-Path $publishRoot "$Runtime-$Version"))
    if (-not $publish.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publish cleanup target must remain inside artifacts/publish.'
    }
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $release, $publish | Out-Null
    if (-not $SkipTests) {
        dotnet test WarThunderMapHelper.slnx -c Release --logger trx --results-directory artifacts/tests
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
    dotnet publish src/MapHelper.Desktop/MapHelper.Desktop.csproj -c Release -r $Runtime --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    & $Python scripts/check_dependency_licenses.py
    if ($LASTEXITCODE -ne 0) { throw 'Dependency license compatibility check failed.' }
    Copy-Item -Path '*.md' -Destination $publish
    Copy-Item -LiteralPath LICENSE -Destination $publish
    Copy-Item -LiteralPath docs -Destination (Join-Path $publish 'docs') -Recurse
    $screenshots = Join-Path $publish 'packaging\flatpak\screenshots'
    New-Item -ItemType Directory -Force -Path $screenshots | Out-Null
    Copy-Item -LiteralPath packaging/flatpak/screenshots/map.png -Destination $screenshots
    & $Python tools/install-flatpak-license-notices.py --assets-file src/MapHelper.Desktop/obj/project.assets.json --output-dir (Join-Path $publish 'THIRD_PARTY_LICENSES') --supplemental-dir packaging/flatpak/licenses
    if ($LASTEXITCODE -ne 0) { throw 'Third-party license collection failed.' }
    & $Python scripts/verify-publish.py $Version --runtime $Runtime
    if ($LASTEXITCODE -ne 0) { throw 'Publish payload verification failed.' }
    $platform = $Runtime.Substring(4)
    dotnet build packaging/windows/MapHelper.Setup.wixproj -c Release "-p:InstallerPlatform=$platform" "-p:PublishDir=$publish" -o $release
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
    & (Join-Path $projectRoot 'scripts\verify-msi.ps1') -MsiPath (Join-Path $release "WarThunderMapHelper-$Version-$Runtime.msi") -PublishDir $publish -Version $Version -Runtime $Runtime
    Get-FileHash -LiteralPath (Join-Path $release "WarThunderMapHelper-$Version-$Runtime.msi") -Algorithm SHA256 | Format-Table -AutoSize
} finally { Pop-Location }
