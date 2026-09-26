<#
.SYNOPSIS
Downloads a published stable release's native MSIs and creates WinGet manifests.
.DESCRIPTION
Requires Python 3.11+, Windows Installer COM, and optionally GH_TOKEN for GitHub
API requests. Public asset downloads never receive that token. Validates the
complete release, GitHub asset SHA-256 digests, and actual MSI identity before writing manifests.
Does not install the application, create a pull request, or publish anything.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseTag,
    [string]$OutputRoot = 'artifacts/winget',
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($ReleaseTag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'ReleaseTag must be stable vMAJOR.MINOR.PATCH.'
}
$version = $ReleaseTag.Substring(1)
$root = Split-Path -Parent $PSScriptRoot
$metadataPath = Join-Path $root 'packaging/winget/package.metadata.json'
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$repository = 'VoltKraft/WarThunder_Map_Helper'
if ($metadata.repository -cne $repository) { throw 'Unexpected WinGet upstream repository.' }
$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw 'OutputRoot must be empty or absent.' }
}
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'WarThunderMapHelper-release'; 'X-GitHub-Api-Version' = '2022-11-28' }
if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/tags/$ReleaseTag" -Headers $headers
if ($release.draft -or $release.prerelease -or $release.tag_name -cne $ReleaseTag -or -not $release.published_at) {
    throw 'The selected release must be published and stable.'
}
$expectedNames = @()
foreach ($arch in @('x64', 'arm64')) {
    $expectedNames += @("WarThunderMapHelper-$version-win-$arch.msi", "WarThunderMapHelper-$version-linux-$arch.flatpak")
}
if ($release.assets.Count -ne $expectedNames.Count) { throw 'The release must contain exactly four MSI and Flatpak packages.' }
$assets = @{}
foreach ($asset in $release.assets) {
    if ($asset.name -cnotin $expectedNames -or $assets.ContainsKey($asset.name) -or $asset.size -le 0 -or $asset.state -cne 'uploaded') {
        throw 'Release assets are incomplete, duplicated, or invalid.'
    }
    $expectedUrl = "https://github.com/$repository/releases/download/$ReleaseTag/$($asset.name)"
    if ($asset.browser_download_url -cne $expectedUrl) { throw "Unexpected asset URL: $($asset.name)" }
    if ($asset.digest -cnotmatch '^sha256:[0-9a-f]{64}$') { throw "Missing or invalid GitHub asset SHA-256: $($asset.name)" }
    $assets[$asset.name] = $asset
}
[void](New-Item -ItemType Directory -Path $output -Force)
function Read-MsiIdentity([string]$Path) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $null; $view = $null; $summary = $null
    try {
        $database = $installer.OpenDatabase($Path, 0)
        $view = $database.OpenView('SELECT `Property`, `Value` FROM `Property`')
        [void]$view.Execute()
        $properties = @{}
        while ($record = $view.Fetch()) {
            try { $properties[[string]$record.StringData(1)] = [string]$record.StringData(2) }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
        $summary = $database.SummaryInformation(0)
        return @{ Properties = $properties; Template = [string]$summary.Property(7) }
    }
    finally {
        if ($summary) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) }
        if ($view) { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
        if ($database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    }
}

$installers = @()
foreach ($architecture in @('x64', 'arm64')) {
    $name = [string]$metadata.releaseAssetNameTemplates.$architecture.Replace('{version}', $version)
    $path = Join-Path $output $name
    Invoke-WebRequest -Uri $assets[$name].browser_download_url -OutFile $path
    $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($digest -ine $assets[$name].digest.Substring(7)) { throw "Release checksum mismatch: $name" }
    $identity = Read-MsiIdentity $path
    $properties = $identity.Properties
    $platform = if ($architecture -eq 'arm64') { 'Arm64' } else { 'x64' }
    $productName = if ($architecture -eq 'arm64') { 'War Thunder Map Helper (ARM64)' } else { 'War Thunder Map Helper' }
    if ($identity.Template.Split(';')[0] -cne $platform -or $properties.ProductVersion -cne $version -or
        $properties.ProductName -cne $productName -or $properties.Manufacturer -cne 'VoltKraft' -or
        $properties.ProductLanguage -cne '1033' -or $properties['ALLUSERS']) {
        throw "MSI architecture, language, scope, or product identity mismatch: $name"
    }
    $installers += [ordered]@{ architecture = $architecture; url = [string]$assets[$name].browser_download_url; sha256 = $digest; productCode = [string]$properties.ProductCode }
}
$installerPath = Join-Path $output 'installers.json'
[IO.File]::WriteAllText($installerPath, ($installers | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
$manifestPath = Join-Path $output 'manifests'
$releaseDate = ([DateTimeOffset]$release.published_at).UtcDateTime.ToString('yyyy-MM-dd')
& $Python (Join-Path $PSScriptRoot 'prepare-winget-release-manifests.py') --metadata $metadataPath --installers $installerPath --output-dir $manifestPath --version $version --release-date $releaseDate
if ($LASTEXITCODE -ne 0) { throw 'WinGet manifest generation failed.' }
Write-Output "Prepared WinGet manifests: $manifestPath"
