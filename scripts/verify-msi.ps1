param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Version,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase([IO.Path]::GetFullPath($MsiPath), 0)

function Read-MsiRows([string]$Query, [string[]]$Columns) {
    $view = $database.OpenView($Query)
    [void]$view.Execute()
    try {
        while ($record = $view.Fetch()) {
            try {
                $entry = [ordered]@{}
                for ($i = 0; $i -lt $Columns.Count; $i++) {
                    $entry[$Columns[$i]] = $record.StringData($i + 1)
                }
                [pscustomobject]$entry
            } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    } finally {
        [void]$view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

try {
    $properties = @{}
    Read-MsiRows 'SELECT `Property`, `Value` FROM `Property`' @('Name', 'Value') |
        ForEach-Object { $properties[$_.Name] = $_.Value }
    if ($properties['ProductVersion'] -ne $Version) { throw 'MSI version does not match the release.' }
    if ($properties['ARPPRODUCTICON'] -ne 'ProgramIcon.ico') { throw 'Installed application icon is missing.' }
    if ($properties['ProductLanguage'] -ne '1033') { throw 'Installer language must be English.' }
    if ($properties['ALLUSERS']) { throw 'The installer must remain per-user.' }
    $expectedUpgradeCode = if ($Runtime -eq 'win-x64') { '{5A824E1E-2A50-432A-BF1E-7D3D13879662}' } else { '{A16566EF-1A42-4CC5-9A69-9E356C479E74}' }
    if ($properties['UpgradeCode'] -ne $expectedUpgradeCode) { throw 'Unexpected installer upgrade identity.' }
    $summary = $database.SummaryInformation(0)
    try {
        $platform = [string]$summary.Property(7)
        $expectedPlatform = if ($Runtime -eq 'win-x64') { 'x64' } else { 'Arm64' }
        if ($platform.Split(';')[0] -ne $expectedPlatform) { throw "MSI architecture does not match $Runtime." }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) }
    $directories = @(Read-MsiRows 'SELECT `Directory`, `DefaultDir` FROM `Directory`' @('Id', 'Name'))
    $installDirectory = @($directories | Where-Object { $_.Id -eq 'INSTALLFOLDER' })
    $expectedDirectory = if ($Runtime -eq 'win-x64') { 'WarThunderMapHelper' } else { 'WarThunderMapHelper-arm64' }
    if ($installDirectory.Count -ne 1 -or $installDirectory[0].Name.Split('|')[-1] -ne $expectedDirectory) { throw 'Installer payload directory is incorrect.' }
    $upgradeRows = @(Read-MsiRows 'SELECT `Language` FROM `Upgrade`' @('Language'))
    if ($upgradeRows.Count -eq 0 -or @($upgradeRows | Where-Object { $_.Language }).Count -gt 0) { throw 'Upgrade detection must include the original German installer.' }
    $msiFiles = @(Read-MsiRows 'SELECT `File`, `FileSize` FROM `File`' @('Id', 'Size'))
    $publishFiles = @(Get-ChildItem -LiteralPath $PublishDir -File -Recurse)
    if ($msiFiles.Count -ne $publishFiles.Count) { throw 'MSI payload file count does not match the publish directory.' }
    foreach ($nativeFile in @('WarThunderMapHelper.exe', 'coreclr.dll', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll')) {
        $stream = [IO.File]::OpenRead((Join-Path $PublishDir $nativeFile))
        $reader = New-Object IO.BinaryReader($stream)
        try {
            if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid PE binary: $nativeFile" }
            $stream.Position = 0x3C
            $offset = $reader.ReadInt32()
            if ($offset -lt 0 -or $offset -gt ($stream.Length - 6)) { throw "Invalid PE header offset: $nativeFile" }
            $stream.Position = $offset
            if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $nativeFile" }
            $expectedMachine = if ($Runtime -eq 'win-x64') { 0x8664 } else { 0xAA64 }
            if ($reader.ReadUInt16() -ne $expectedMachine) { throw "Incorrect native architecture: $nativeFile" }
        } finally { $reader.Dispose(); $stream.Dispose() }
    }

    $shortcuts = @(Read-MsiRows 'SELECT `Shortcut`, `Directory_`, `Target`, `Icon_`, `WkDir`, `Component_` FROM `Shortcut`' @('Id', 'Directory', 'Target', 'Icon', 'WorkingDirectory', 'Component'))
    foreach ($expected in @(
        @{ Id = 'DesktopShortcut'; Directory = 'DesktopFolder'; Component = 'DesktopShortcutComponent' },
        @{ Id = 'StartShortcut'; Directory = 'AppMenuFolder'; Component = 'MenuShortcut' }
    )) {
        $shortcut = @($shortcuts | Where-Object { $_.Id -eq $expected.Id })
        if ($shortcut.Count -ne 1 -or $shortcut[0].Directory -ne $expected.Directory -or
            $shortcut[0].Component -ne $expected.Component -or
            $shortcut[0].Target -ne '[INSTALLFOLDER]WarThunderMapHelper.exe' -or
            $shortcut[0].WorkingDirectory -ne 'INSTALLFOLDER' -or $shortcut[0].Icon -ne 'ProgramIcon.ico') {
            throw "Incorrect or missing shortcut: $($expected.Id)"
        }
    }
    $components = @(Read-MsiRows 'SELECT `Feature_`, `Component_` FROM `FeatureComponents`' @('Feature', 'Component'))
    if (@($components | Where-Object { $_.Feature -eq 'Complete' -and $_.Component -eq 'DesktopShortcutComponent' }).Count -ne 1) {
        throw 'Desktop shortcut is not included in the installed feature.'
    }

    $iconView = $database.OpenView('SELECT `Name`, `Data` FROM `Icon`')
    [void]$iconView.Execute()
    $iconBytes = 0
    try {
        while ($record = $iconView.Fetch()) {
            try { if ($record.StringData(1) -eq 'ProgramIcon.ico') { $iconBytes = $record.DataSize(2) } }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    } finally {
        [void]$iconView.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($iconView)
    }
    $sourceIcon = Get-Item -LiteralPath (Join-Path $PublishDir 'Assets\branding\app.ico')
    if ($iconBytes -le 0 -or $iconBytes -ne $sourceIcon.Length) { throw 'Embedded MSI icon does not match the publish output.' }
    $exeVersion = (Get-Item -LiteralPath (Join-Path $PublishDir 'WarThunderMapHelper.exe')).VersionInfo.ProductVersion
    if ($exeVersion.Split('+')[0] -ne $Version) { throw 'EXE version does not match the MSI.' }

    $result = [ordered]@{
        Version = $Version
        Runtime = $Runtime
        ProductCode = $properties['ProductCode']
        UpgradeCode = $properties['UpgradeCode']
        PayloadFiles = $msiFiles.Count
        DesktopShortcut = $true
        StartMenuShortcut = $true
        EmbeddedIconBytes = $iconBytes
        ExeVersion = $exeVersion
        Shortcuts = $shortcuts
    }
    $outputPath = Join-Path $PSScriptRoot "..\artifacts\msi-verification-$Version-$Runtime.json"
    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $outputPath -Encoding utf8
    Write-Output "Verified MSI $Version ($Runtime): payload, architecture, upgrade identity, shortcuts, icon and version."
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
