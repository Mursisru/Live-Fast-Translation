#Requires -Version 5.1
<#
    Fast smoke check for Live-Fast Translation.

    Launches the app in an isolated profile, verifies the settings window,
    keyboard search, section navigation, maximize/restore and Escape handling.
    Takes a few seconds and is safe to run before every commit.

    Run:
      powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts/smoke.ps1
#>
param(
    [string]$ExePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

. (Join-Path $PSScriptRoot 'uia-helpers.ps1')

$script:StartedAt = Get-Date
$script:Results = New-Object System.Collections.Generic.List[object]
$script:Screenshots = New-Object System.Collections.Generic.List[string]
$script:Process = $null
$script:Window = $null
$script:ProfileDir = $null
$script:ArtifactsDir = $null
$script:Failures = 0

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ExePath) {
    $ExePath = Join-Path $script:RepoRoot 'src\LiveFastTranslation\bin\Debug\net8.0-windows\LiveFastTranslation.exe'
}
$script:ExePath = $ExePath
$script:WindowTitle = 'Live-Fast Translation ' + [char]0x2014 + ' ' + (-join ([char[]]@(
    0x041D, 0x0430, 0x0441, 0x0442, 0x0440, 0x043E, 0x0439, 0x043A, 0x0438)))
$script:SearchWord = -join ([char[]]@(0x0442, 0x0435, 0x043C, 0x0430))

function Add-Result {
    param([string]$Name, [string]$Status, [int]$DurationMs, [string]$Detail)
    $script:Results.Add([pscustomobject]@{
        name = $Name
        status = $Status
        durationMs = $DurationMs
        detail = $Detail
    })
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Body,
        [switch]$Critical
    )

    Write-Host "[STEP] $Name" -ForegroundColor Cyan
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Body
        $watch.Stop()
        Add-Result -Name $Name -Status 'pass' -DurationMs ([int]$watch.ElapsedMilliseconds) -Detail ''
        Write-Host ("[PASS] {0} ({1} ms)" -f $Name, $watch.ElapsedMilliseconds) -ForegroundColor Green
    } catch {
        $watch.Stop()
        $detail = $_.Exception.Message
        $script:Failures++
        Add-Result -Name $Name -Status 'fail' -DurationMs ([int]$watch.ElapsedMilliseconds) -Detail $detail
        Write-Host ("[FAIL] {0}: {1}" -f $Name, $detail) -ForegroundColor Red
        if ($Critical) { throw }
    }
}

function Find-Now {
    param([string]$AutomationId, $Root = $null)
    if ($null -eq $Root) { $Root = $script:Window }
    if ($null -eq $Root) { return $null }
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId))
}

function Wait-ForElement {
    param([string]$AutomationId, [int]$TimeoutMs = 6000, $Root = $null)
    return Wait-Until -TimeoutMs $TimeoutMs -IntervalMs 120 -Condition {
        Find-Now -AutomationId $AutomationId -Root $Root
    } -TimeoutMessage "Element '$AutomationId' did not appear within ${TimeoutMs}ms"
}

function Get-NavItemCount {
    $nav = Find-Now -AutomationId 'NavSections'
    if (-not $nav) { return 0 }
    return $nav.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem)).Count
}

function Select-Nav {
    param([Parameter(Mandatory)][string]$Section)
    $item = Wait-ForElement -AutomationId $Section
    try { Scroll-IntoView $item } catch { }
    (Get-Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 260
}

function Save-Shot {
    param([string]$Name)
    $path = Save-Screenshot -Name $Name -Element $script:Window
    $script:Screenshots.Add($path)
    Write-Host "       screenshot: $path" -ForegroundColor DarkGray
}

$runStamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:ArtifactsDir = Join-Path $PSScriptRoot ('artifacts\smoke_' + $runStamp)
Set-ArtifactsDir -Path $script:ArtifactsDir

try {
    Invoke-Step -Name 'Launch application in an isolated profile' -Critical {
        Assert-True (Test-Path -LiteralPath $script:ExePath) "Application exe not found: $($script:ExePath)"
        $script:ProfileDir = Join-Path $env:TEMP ('LFT_smoke_' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:ProfileDir -Force | Out-Null

        $script:Process = Start-AppUnderTest -Path $script:ExePath -Environment @{
            LFT_DATA_DIR = $script:ProfileDir
            LFT_DISABLE_AUTOSTART = '1'
        }
        Assert-True ($null -ne $script:Process) 'Application process was not started.'
        $script:Window = Wait-Window -ProcessId $script:Process.Id -Title $script:WindowTitle -Exact -TimeoutMs 30000
        Start-Sleep -Milliseconds 1000
        Set-WindowForeground $script:Window
        $null = Wait-ForElement -AutomationId 'SettingsTitleText' -TimeoutMs 20000
        Assert-True (-not $script:Process.HasExited) 'Application exited during startup.'
        Save-Shot -Name '01_settings_window'
    }

    Invoke-Step -Name 'Ctrl+F focuses the settings search field' {
        Set-WindowForeground $script:Window
        Focus-Element (Wait-ForElement -AutomationId 'SettingsSearchBox')
        Send-VirtualKeys -VirtualKeys @(0x11, 0x46)
        Start-Sleep -Milliseconds 250
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        Assert-True ($focused.Current.AutomationId -eq 'SettingsSearchBox') 'Ctrl+F must focus SettingsSearchBox.'
    }

    Invoke-Step -Name 'Search filters the navigation, Escape clears it' {
        $search = Wait-ForElement -AutomationId 'SettingsSearchBox'
        Set-Value $search $script:SearchWord
        Start-Sleep -Milliseconds 300
        Assert-True ((Get-NavItemCount) -eq 1) 'Russian search for the word "theme" must leave one section.'

        Send-VirtualKeys -VirtualKeys @(0x1B)
        Start-Sleep -Milliseconds 300
        Assert-True ($null -ne (Find-Now -AutomationId 'SettingsTitleText')) 'Escape must not close the window while the search field has text.'
        Assert-True ((Get-NavItemCount) -eq 6) 'Escape must clear the search and restore all sections.'
        Assert-True ((Get-Value $search) -eq '') 'Search field must be empty after Escape.'
        Save-Shot -Name '02_search_cleared'
    }

    Invoke-Step -Name 'All six sections render their anchor control' {
        $sections = [ordered]@{
            Provider = 'TestArgosButton'
            Replacement = 'StrategyCombo'
            Hotkeys = 'TranslateHotkeyBox'
            AiMode = 'AiModeEnabledCheckBox'
            Appearance = 'ThemeGallery'
            Advanced = 'AutoStartCheckBox'
        }

        foreach ($section in $sections.Keys) {
            Select-Nav -Section $section
            $anchor = Wait-ForElement -AutomationId $sections[$section] -TimeoutMs 5000
            Assert-True ($null -ne $anchor) "Section $section did not render its anchor control."
        }

        Select-Nav -Section 'Provider'
        Save-Shot -Name '03_provider_section'
    }

    Invoke-Step -Name 'Theme gallery shows 64 cards and selects by id' {
        Select-Nav -Section 'Appearance'
        $gallery = Wait-ForElement -AutomationId 'ThemeGallery' -TimeoutMs 6000
        $cards = $gallery.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem))
        Assert-True ($cards.Count -eq 64) ("Theme gallery must expose 64 cards, found {0}." -f $cards.Count)

        $darkPaper = Wait-ForElement -AutomationId 'dark-paper' -TimeoutMs 5000
        $darkPattern = Get-Pattern $darkPaper ([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $darkPattern.Select()
        Start-Sleep -Milliseconds 500
        Assert-True ($darkPattern.Current.IsSelected) 'Selecting the dark-paper card must select it.'

        $lightSky = Wait-ForElement -AutomationId 'light-sky' -TimeoutMs 5000
        (Get-Pattern $lightSky ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
        Start-Sleep -Milliseconds 500

        $minimal = Wait-ForElement -AutomationId 'MinimalOverlayCheckBox' -TimeoutMs 4000
        Assert-True ($null -ne $minimal) 'Minimal overlay checkbox is missing.'
        $background = Wait-ForElement -AutomationId 'OverlayBackgroundTransparencySlider' -TimeoutMs 4000
        Assert-True ($null -ne $background) 'Background transparency slider is missing.'
        Save-Shot -Name '04_theme_gallery'
    }

    Invoke-Step -Name 'Maximize and restore work from the title bar' {
        $maximize = Wait-ForElement -AutomationId 'MaximizeButton'
        Invoke-Element $maximize
        Start-Sleep -Milliseconds 600
        $windowPattern = Get-Pattern $script:Window ([System.Windows.Automation.WindowPattern]::Pattern)
        Assert-True ($windowPattern.Current.WindowVisualState -eq 'Maximized') 'Window must be maximized after the first click.'
        Invoke-Element $maximize
        Start-Sleep -Milliseconds 600
        Assert-True ($windowPattern.Current.WindowVisualState -eq 'Normal') 'Window must be restored after the second click.'
    }

    Invoke-Step -Name 'Escape closes a clean settings window and the app keeps running' {
        Set-WindowForeground $script:Window
        Send-VirtualKeys -VirtualKeys @(0x1B)
        Wait-WindowGone -ProcessId $script:Process.Id -Title $script:WindowTitle -TimeoutMs 8000 | Out-Null
        Start-Sleep -Milliseconds 500
        Assert-True (-not $script:Process.HasExited) 'Application must stay alive in the tray after closing settings.'
    }
}
finally {
    if ($script:Process -and -not $script:Process.HasExited) {
        Stop-AppHard -ProcessId $script:Process.Id -ExePath $script:ExePath
    }
    if ($script:ProfileDir -and (Test-Path -LiteralPath $script:ProfileDir)) {
        Remove-Item -LiteralPath $script:ProfileDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    $finishedAt = Get-Date
    $passed = @($script:Results | Where-Object { $_.status -eq 'pass' }).Count
    $failed = @($script:Results | Where-Object { $_.status -eq 'fail' }).Count
    $report = [pscustomobject]@{
        startedAt = $script:StartedAt.ToString('o')
        finishedAt = $finishedAt.ToString('o')
        durationSeconds = [Math]::Round(($finishedAt - $script:StartedAt).TotalSeconds, 1)
        exe = $script:ExePath
        artifacts = $script:ArtifactsDir
        summary = [pscustomobject]@{
            total = $script:Results.Count
            passed = $passed
            failed = $failed
        }
        steps = $script:Results
        screenshots = $script:Screenshots
    }
    $reportPath = Join-Path $script:ArtifactsDir 'report.json'
    [System.IO.File]::WriteAllText(
        $reportPath,
        ($report | ConvertTo-Json -Depth 6),
        (New-Object System.Text.UTF8Encoding($false)))

    Write-Host ''
    Write-Host '==== SMOKE RESULT ====' -ForegroundColor White
    Write-Host ("Artifacts: {0}" -f $script:ArtifactsDir) -ForegroundColor DarkGray
    Write-Host ("Report:    {0}" -f $reportPath) -ForegroundColor DarkGray
    Write-Host ("Passed: {0}  Failed: {1}" -f $passed, $failed) -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Red' })
    if ($failed -gt 0) {
        foreach ($failure in ($script:Results | Where-Object { $_.status -eq 'fail' })) {
            Write-Host (" - {0}: {1}" -f $failure.name, $failure.detail) -ForegroundColor Red
        }
    }
}

if ($failed -gt 0) { exit 1 }
exit 0
