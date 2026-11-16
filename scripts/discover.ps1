#Requires -Version 5.1
<#
    Dump the UI Automation tree of Live-Fast Translation settings sections.

    Useful when an AutomationId changes or a new control must be addressed by
    the test scripts: run this, open the generated .txt files and copy the ids.

    Run:
      powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts/discover.ps1
      powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts/discover.ps1 -Section Appearance
#>
param(
    [ValidateSet('All', 'Provider', 'Replacement', 'Hotkeys', 'AiMode', 'Appearance', 'Advanced')]
    [string]$Section = 'All',
    [string]$ExePath = '',
    [switch]$KeepProfile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

. (Join-Path $PSScriptRoot 'uia-helpers.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ExePath) {
    $ExePath = Join-Path $repo 'src\LiveFastTranslation\bin\Debug\net8.0-windows\LiveFastTranslation.exe'
}

$title = 'Live-Fast Translation ' + [char]0x2014 + ' ' + (-join ([char[]]@(
    0x041D, 0x0430, 0x0441, 0x0442, 0x0440, 0x043E, 0x0439, 0x043A, 0x0438)))
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$artifacts = Join-Path $PSScriptRoot ('artifacts\discover_' + $stamp)
Set-ArtifactsDir -Path $artifacts

$profileDir = Join-Path $env:TEMP ('LFT_discover_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profileDir -Force | Out-Null

$sections = if ($Section -eq 'All') {
    @('Provider', 'Replacement', 'Hotkeys', 'AiMode', 'Appearance', 'Advanced')
} else {
    @($Section)
}

$process = $null
try {
    $process = Start-AppUnderTest -Path $ExePath -Environment @{
        LFT_DATA_DIR = $profileDir
        LFT_DISABLE_AUTOSTART = '1'
    }

    $window = Wait-Window -ProcessId $process.Id -Title $title -Exact -TimeoutMs 30000
    Start-Sleep -Milliseconds 1200
    Set-WindowForeground $window

    $navCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'NavSections')
    $nav = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $navCondition)
    if (-not $nav) { throw 'NavSections was not found.' }

    foreach ($name in $sections) {
        $item = $nav.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $name))
        if (-not $item) { throw "Navigation item '$name' was not found." }

        (Get-Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
        Start-Sleep -Milliseconds 500

        $safeName = $name.ToLowerInvariant()
        Save-Screenshot -Name ('discover_' + $safeName) -Element $window | Out-Null
        Dump-UiTree -Element $window -MaxDepth 14 |
            Out-File -FilePath (Join-Path $artifacts ($safeName + '_tree.txt')) -Encoding UTF8

        Write-Host ("[OK] {0}" -f $name) -ForegroundColor Green
    }

    Dump-UiTree -Element $window -MaxDepth 4 |
        Out-File -FilePath (Join-Path $artifacts 'window_tree.txt') -Encoding UTF8
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-AppHard -ProcessId $process.Id -ExePath $ExePath
    }
    if (-not $KeepProfile -and (Test-Path -LiteralPath $profileDir)) {
        Remove-Item -LiteralPath $profileDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($KeepProfile) { Write-Host "Profile kept: $profileDir" -ForegroundColor DarkGray }
    Write-Host "Artifacts: $artifacts" -ForegroundColor DarkGray
}
