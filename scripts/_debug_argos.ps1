#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
. (Join-Path $PSScriptRoot 'uia-helpers.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = Join-Path $repo 'src\LiveFastTranslation\bin\Debug\net8.0-windows\LiveFastTranslation.exe'
$profile = Join-Path $env:TEMP ('LFT_argos_debug_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profile -Force | Out-Null
$artifacts = Join-Path $PSScriptRoot 'artifacts\_debug_argos'
Set-ArtifactsDir -Path $artifacts

$envMap = @{ LFT_DATA_DIR = $profile; LFT_DISABLE_AUTOSTART = '1' }
$process = $null
try {
    $process = Start-AppUnderTest -Path $exe -Environment $envMap
    $title = 'Live-Fast Translation ' + [char]0x2014 + ' ' + (-join ([char[]]@(0x041D,0x0430,0x0441,0x0442,0x0440,0x043E,0x0439,0x043A,0x0438)))
    $window = Wait-Window -ProcessId $process.Id -Title $title -Exact -TimeoutMs 30000
    Start-Sleep -Milliseconds 1200
    Write-Host ("Window: '{0}'" -f $window.Current.Name)
    Save-Screenshot -Name 'argos_start' -Element $window | Out-Null
    Dump-UiTree -Element $window -MaxDepth 4 | Out-File -FilePath (Join-Path $artifacts 'tree_before.txt') -Encoding UTF8
    Set-WindowForeground $window

    $button = $null
    $findWatch = [System.Diagnostics.Stopwatch]::StartNew()
    while ($findWatch.ElapsedMilliseconds -lt 15000 -and -not $button) {
        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'TestArgosButton')
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if (-not $button) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $button) {
        Dump-UiTree -Element $window -MaxDepth 8 | Out-File -FilePath (Join-Path $artifacts 'tree_after.txt') -Encoding UTF8
        throw 'TestArgosButton not found with direct FindFirst.'
    }
    Write-Host ("Button found after {0} ms; enabled={1}" -f $findWatch.ElapsedMilliseconds, $button.Current.IsEnabled)
    Invoke-Element $button
    Write-Host "Invoked."

    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 2000
        $button = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'TestArgosButton'))
        $badgeCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ArgosTestBadge')
        $statusCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ArgosTestStatusText')
        $badge = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $badgeCondition)
        $status = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $statusCondition)
        $badgeText = if ($badge) { [string]$badge.Current.Name } else { '<none>' }
        $statusText = if ($status) { [string]$status.Current.Name } else { '<none>' }
        $btnEnabled = $button.Current.IsEnabled
        $py = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'py*' -or $_.ProcessName -like 'python*' })
        $pyInfo = ($py | ForEach-Object { "$($_.Id):$($_.ProcessName):$([int]($_.StartTime.ToUniversalTime() - [datetime]'1970-01-01').TotalSeconds)" }) -join ','
        Write-Host ("t={0}s btnEnabled={1} badge='{2}' status='{3}' py=[{4}]" -f (($i + 1) * 2), $btnEnabled, $badgeText, $statusText, $pyInfo)
        if ($i -eq 1) { Save-Screenshot -Name 'argos_4s' -Element $window | Out-Null }
        if ($i -eq 7) { Save-Screenshot -Name 'argos_16s' -Element $window | Out-Null }
        if ($i -eq 19) { Save-Screenshot -Name 'argos_40s' -Element $window | Out-Null }
        if ($badgeText -ne '<none>' -and $badgeText -ne '') { break }
    }

    Save-Screenshot -Name 'argos_final' -Element $window | Out-Null
    Dump-UiTree -Element $window -MaxDepth 6 | Out-File -FilePath (Join-Path $artifacts 'tree.txt') -Encoding UTF8
    $log = Join-Path $profile 'logs\app.log'
    if (Test-Path -LiteralPath $log) {
        Copy-Item -LiteralPath $log -Destination (Join-Path $artifacts 'app.log')
        Write-Host '--- log tail ---'
        Get-Content -LiteralPath $log -Tail 40
    }
} finally {
    if ($process -and -not $process.HasExited) { Stop-AppHard -ProcessId $process.Id -ExePath $exe }
    Write-Host "Profile kept at: $profile"
    Write-Host "Artifacts: $artifacts"
}
