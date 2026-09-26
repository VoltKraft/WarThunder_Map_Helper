param(
    [Parameter(Mandatory=$true)][string]$PublishDir,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [ValidateSet('x64', 'arm64')][string]$Platform = 'x64'
)
$ErrorActionPreference = 'Stop'
$publishRoot = [IO.Path]::GetFullPath($PublishDir).TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $publishRoot 'WarThunderMapHelper.exe'))) { throw 'Publish directory does not contain the application.' }
$registryRoot = if ($Platform -eq 'x64') { 'Software\WarThunderMapHelper' } else { 'Software\WarThunderMapHelper\arm64' }
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputFile))) | Out-Null
$sha = [Security.Cryptography.SHA256]::Create()
function StableId([string]$prefix, [string]$relative) {
    $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($relative.ToLowerInvariant()))
    return $prefix + ([BitConverter]::ToString($hash).Replace('-', '').Substring(0, 32))
}
function StableGuid([string]$relative) {
    # Existing x64 identities remain stable; ARM64 files cannot share those components.
    $namespace = if ($Platform -eq 'x64') { 'WarThunderMapHelper/component/' } else { 'WarThunderMapHelper/arm64/component/' }
    $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($namespace + $relative).ToLowerInvariant()))
    return [Guid]::ParseExact(([BitConverter]::ToString($hash).Replace('-', '').Substring(0, 32)), 'N').ToString('D')
}
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$writer = [Xml.XmlWriter]::Create($OutputFile, $settings)
$ns = 'http://wixtoolset.org/schemas/v4/wxs'
try {
    $writer.WriteStartElement('Wix', $ns)
    $writer.WriteStartElement('Fragment', $ns)
    $directories = @(Get-ChildItem -LiteralPath $publishRoot -Directory -Recurse | Sort-Object FullName)
    foreach ($directory in $directories) {
        $relative = $directory.FullName.Substring($publishRoot.Length + 1)
        $parentRelative = [IO.Path]::GetDirectoryName($relative)
        $parentId = if ($parentRelative) { StableId 'dir' $parentRelative } else { 'INSTALLFOLDER' }
        $writer.WriteStartElement('DirectoryRef', $ns); $writer.WriteAttributeString('Id', $parentId)
        $writer.WriteStartElement('Directory', $ns); $writer.WriteAttributeString('Id', (StableId 'dir' $relative)); $writer.WriteAttributeString('Name', $directory.Name)
        $writer.WriteEndElement(); $writer.WriteEndElement()
    }
    $writer.WriteStartElement('ComponentGroup', $ns); $writer.WriteAttributeString('Id', 'ApplicationFiles')
    $cleanedDirectories = @{}
    foreach ($file in (Get-ChildItem -LiteralPath $publishRoot -File -Recurse | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($publishRoot.Length + 1)
        $directoryRelative = [IO.Path]::GetDirectoryName($relative)
        $directoryId = if ($directoryRelative) { StableId 'dir' $directoryRelative } else { 'INSTALLFOLDER' }
        $componentId = StableId 'cmp' $relative
        $writer.WriteStartElement('Component', $ns); $writer.WriteAttributeString('Id', $componentId); $writer.WriteAttributeString('Directory', $directoryId); $writer.WriteAttributeString('Guid', (StableGuid $relative))
        $writer.WriteStartElement('File', $ns); $writer.WriteAttributeString('Id', (StableId 'file' $relative)); $writer.WriteAttributeString('Source', $file.FullName); $writer.WriteAttributeString('KeyPath', 'no'); $writer.WriteEndElement()
        $writer.WriteStartElement('RegistryValue', $ns); $writer.WriteAttributeString('Root', 'HKCU'); $writer.WriteAttributeString('Key', ($registryRoot + '\Files')); $writer.WriteAttributeString('Name', $componentId); $writer.WriteAttributeString('Type', 'integer'); $writer.WriteAttributeString('Value', '1'); $writer.WriteAttributeString('KeyPath', 'yes'); $writer.WriteEndElement()
        if (-not $cleanedDirectories.ContainsKey($directoryId)) {
            $writer.WriteStartElement('RemoveFolder', $ns); $writer.WriteAttributeString('Id', ('remove' + $directoryId)); $writer.WriteAttributeString('Directory', $directoryId); $writer.WriteAttributeString('On', 'uninstall'); $writer.WriteEndElement()
            $cleanedDirectories[$directoryId] = $true
        }
        $writer.WriteEndElement()
    }
    # Parent directories with no direct files still require uninstall cleanup.
    foreach ($directory in $directories) {
        $relative = $directory.FullName.Substring($publishRoot.Length + 1); $id = StableId 'dir' $relative
        if ($cleanedDirectories.ContainsKey($id)) { continue }
        $writer.WriteStartElement('Component', $ns); $writer.WriteAttributeString('Id', ('cleanup' + $id)); $writer.WriteAttributeString('Directory', $id); $writer.WriteAttributeString('Guid', '*')
        $writer.WriteStartElement('RegistryValue', $ns); $writer.WriteAttributeString('Root', 'HKCU'); $writer.WriteAttributeString('Key', ($registryRoot + '\Folders')); $writer.WriteAttributeString('Name', $id); $writer.WriteAttributeString('Type', 'integer'); $writer.WriteAttributeString('Value', '1'); $writer.WriteAttributeString('KeyPath', 'yes'); $writer.WriteEndElement()
        $writer.WriteStartElement('RemoveFolder', $ns); $writer.WriteAttributeString('Id', ('remove' + $id)); $writer.WriteAttributeString('On', 'uninstall'); $writer.WriteEndElement(); $writer.WriteEndElement()
    }
    $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndElement()
} finally { $writer.Dispose(); $sha.Dispose() }
