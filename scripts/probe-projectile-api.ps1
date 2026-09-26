param([ValidateRange(1,600)][int]$Seconds = 180, [string]$Output = 'artifacts/projectile-api-probe')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$started = [DateTimeOffset]::Now
$timer = [Diagnostics.Stopwatch]::StartNew()
$observed = @{}
$fields = @{}
$errors = @{}
$frames = 0
$nextInstruments = 0.0
$nextReport = 15.0
while ($timer.Elapsed.TotalSeconds -lt $Seconds) {
    try {
        # Windows PowerShell 5.1 returns a JSON array as one pipeline item.
        # Assign directly so foreach visits individual objects instead of an array wrapper.
        $objects = Invoke-RestMethod -Uri http://127.0.0.1:8111/map_obj.json -TimeoutSec 2
        $frames++
        foreach ($item in $objects) {
            $key = [string]$item.type + '|' + [string]$item.icon
            if (-not $observed.ContainsKey($key)) {
                $observed[$key] = [ordered]@{ Type=$item.type; Icon=$item.icon; FirstSeconds=$timer.Elapsed.TotalSeconds; LastSeconds=$timer.Elapsed.TotalSeconds; Samples=0; Example=$item }
                Write-Output ('Observed map object: ' + $key)
            }
            $observed[$key].LastSeconds = $timer.Elapsed.TotalSeconds
            $observed[$key].Samples++
        }
        if ($timer.Elapsed.TotalSeconds -ge $nextInstruments) {
            foreach ($endpoint in @('state','indicators')) {
                $instrument = Invoke-RestMethod -Uri ('http://127.0.0.1:8111/' + $endpoint) -TimeoutSec 2
                foreach ($property in $instrument.PSObject.Properties) {
                    $key = $endpoint + '.' + $property.Name
                    if (-not $fields.ContainsKey($key)) { $fields[$key] = @{ First=$property.Value; Latest=$property.Value; Changes=0; Samples=0 } }
                    if (($fields[$key].Latest | ConvertTo-Json -Compress -Depth 8) -cne ($property.Value | ConvertTo-Json -Compress -Depth 8)) { $fields[$key].Changes++ }
                    $fields[$key].Samples++
                    $fields[$key].Latest = $property.Value
                }
            }
            $nextInstruments = $timer.Elapsed.TotalSeconds + .5
        }
    } catch {
        $message = $_.Exception.Message
        if (-not $errors.ContainsKey($message)) { $errors[$message] = 0 }
        $errors[$message]++
    }
    $summary = [ordered]@{ Started=$started.ToString('O'); Ended=[DateTimeOffset]::Now.ToString('O'); Seconds=$timer.Elapsed.TotalSeconds; MapFrames=$frames; ObjectKinds=@($observed.Values); InstrumentFields=$fields; Errors=$errors }
    if ($timer.Elapsed.TotalSeconds -ge $nextReport) {
        $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Output 'summary.json') -Encoding utf8
        Write-Output ('Checked {0} frames in {1:0} s; {2} type/icon combinations' -f $frames,$timer.Elapsed.TotalSeconds,$observed.Count)
        $nextReport += 15
    }
    Start-Sleep -Milliseconds 80
}
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Output 'summary.json') -Encoding utf8
Write-Output ('Complete: {0} frames; {1}' -f $frames,(Join-Path $Output 'summary.json'))
