#Requires -Version 5.1
<#
    End-to-end UI Automation check for Live-Fast Translation.

    Covers: first run, navigation, search, Argos Translate, AI provider switching,
    hotkey validation/reset, appearance changes, saving, settings.json contents,
    single-instance activation, dark theme, unsaved-changes guard and Esc close.

    Run:
      powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts/test_e2e.ps1

    The app runs against a throwaway profile in %TEMP% (LFT_DATA_DIR) and never
    touches the real %AppData%\LiveFastTranslation folder or the HKCU Run key.
#>
param(
    [string]$ExePath = '',
    [switch]$KeepProfile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

. (Join-Path $PSScriptRoot 'uia-helpers.ps1')

$script:StartedAt = Get-Date
$script:Results = New-Object System.Collections.Generic.List[object]
$script:Screenshots = New-Object System.Collections.Generic.List[string]
$script:Aborted = $false
$script:Process = $null
$script:SecondProcess = $null
$script:Window = $null
$script:ProfileDir = $null
$script:ArtifactsDir = $null

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ExePath) {
    $ExePath = Join-Path $script:RepoRoot 'src\LiveFastTranslation\bin\Debug\net8.0-windows\LiveFastTranslation.exe'
}
$script:ExePath = $ExePath

function U {
    param([int[]]$Code)
    return (-join ($Code | ForEach-Object { [char]$_ }))
}

$script:WordSuccess = U @(0x0423,0x0441,0x043F,0x0435,0x0448,0x043D,0x043E)   # Uspehno
$script:WordError = U @(0x041E,0x0448,0x0438,0x0431,0x043A,0x0430)             # Oshibka
$script:SettingsTitle = 'Live-Fast Translation ' + [char]0x2014 + ' ' + (U @(
    0x041D,0x0430,0x0441,0x0442,0x0440,0x043E,0x0439,0x043A,0x0438))            # Nastroyki
$script:SearchTheme = U @(0x0442,0x0435,0x043C,0x0430)                          # tema
$script:NativeLanguageLabel = U @(
    0x0420,0x043E,0x0434,0x043D,0x043E,0x0439,0x0020,0x044F,0x0437,0x044B,0x043A) # Rodnoy yazyk

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
        [switch]$Critical,
        [switch]$Always
    )

    if ($script:Aborted -and -not $Always) {
        Add-Result -Name $Name -Status 'skipped' -DurationMs 0 -Detail 'Skipped after a critical failure.'
        Write-Host "[SKIP] $Name" -ForegroundColor DarkGray
        return
    }

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
        Add-Result -Name $Name -Status 'fail' -DurationMs ([int]$watch.ElapsedMilliseconds) -Detail $detail
        Write-Host ("[FAIL] {0}: {1}" -f $Name, $detail) -ForegroundColor Red
        if ($Critical) { $script:Aborted = $true }
    }
}

function Save-Shot {
    param([string]$Name, $Element = $null)
    if ($null -eq $Element) { $Element = $script:Window }
    if ($null -eq $Element) { return }
    $path = Save-Screenshot -Name $Name -Element $Element
    $script:Screenshots.Add($path)
    Write-Host "       screenshot: $path" -ForegroundColor DarkGray
}

function Find-Now {
    param([string]$AutomationId, $Root = $null)
    if ($null -eq $Root) { $Root = $script:Window }
    if ($null -eq $Root) { return $null }
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-ForElement {
    param([string]$AutomationId, [int]$TimeoutMs = 8000, $Root = $null)
    if ($null -eq $Root) { $Root = $script:Window }
    return Wait-Until -TimeoutMs $TimeoutMs -IntervalMs 120 -Condition {
        Find-Now -AutomationId $AutomationId -Root $Root
    } -TimeoutMessage "Element '$AutomationId' did not appear in ${TimeoutMs}ms"
}

function Wait-ForElementGone {
    param([string]$AutomationId, [int]$TimeoutMs = 8000, $Root = $null)
    if ($null -eq $Root) { $Root = $script:Window }
    return Wait-Until -TimeoutMs $TimeoutMs -IntervalMs 120 -Condition {
        $null -eq (Find-Now -AutomationId $AutomationId -Root $Root)
    } -TimeoutMessage "Element '$AutomationId' did not disappear in ${TimeoutMs}ms"
}

function Get-NavItemCount {
    $nav = Find-Now -AutomationId 'NavSections'
    if (-not $nav) { return 0 }
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $items = $nav.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    return $items.Count
}

function Wait-NavItemCount {
    param([int]$Expected, [int]$TimeoutMs = 6000)
    return Wait-Until -TimeoutMs $TimeoutMs -IntervalMs 120 -Condition {
        if ((Get-NavItemCount) -eq $Expected) { $true } else { $false }
    } -TimeoutMessage "Navigation list did not reach $Expected item(s)"
}

function Select-Nav {
    param([Parameter(Mandatory)][string]$Section)
    $item = Wait-ForElement -AutomationId $Section
    try { Scroll-IntoView $item } catch { }
    $pattern = Get-Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    Start-Sleep -Milliseconds 280
}

function Set-SearchText {
    param([AllowEmptyString()][string]$Text)
    $box = Wait-ForElement -AutomationId 'SettingsSearchBox'
    Set-Value $box $Text
    Start-Sleep -Milliseconds 220
}

function Set-EditableComboText {
    param([Parameter(Mandatory)]$Element, [Parameter(Mandatory)][string]$Text)
    $valuePattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
        try {
            $valuePattern.SetValue($Text)
            Start-Sleep -Milliseconds 250
            return
        } catch { }
    }
    Focus-Element $Element
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 80
    [System.Windows.Forms.SendKeys]::SendWait($Text)
    Start-Sleep -Milliseconds 250
}

function Get-ComboSelectionNameSafe {
    param($Element)
    try {
        $pattern = Get-Pattern $Element ([System.Windows.Automation.SelectionPattern]::Pattern)
        $selection = $pattern.GetCurrentSelection()
        if ($selection.Count -eq 0) { return '' }
        return [string]$selection[0].Current.Name
    } catch {
        return ''
    }
}

function Read-SettingsJson {
    param([int]$TimeoutMs = 10000)
    $path = Join-Path $script:ProfileDir 'settings.json'
    $null = Wait-Until -TimeoutMs $TimeoutMs -IntervalMs 150 -Condition {
        (Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Length -gt 2)
    } -TimeoutMessage "settings.json was not written: $path"
    Start-Sleep -Milliseconds 250
    return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Get-ImageLuminance {
    param([Parameter(Mandatory)][string]$Path)
    $bitmap = [System.Drawing.Bitmap]::FromFile($Path)
    try {
        $stepX = [Math]::Max(1, [int]($bitmap.Width / 48))
        $stepY = [Math]::Max(1, [int]($bitmap.Height / 48))
        $sum = 0.0
        $count = 0
        for ($x = 0; $x -lt $bitmap.Width; $x += $stepX) {
            for ($y = 0; $y -lt $bitmap.Height; $y += $stepY) {
                $pixel = $bitmap.GetPixel($x, $y)
                $sum += (0.2126 * $pixel.R) + (0.7152 * $pixel.G) + (0.0722 * $pixel.B)
                $count++
            }
        }
        if ($count -eq 0) { return 0.0 }
        return ($sum / $count)
    } finally {
        $bitmap.Dispose()
    }
}

function Assert-SettingsUnchanged {
    $realSettings = Join-Path $env:APPDATA 'LiveFastTranslation\settings.json'
    $existsNow = Test-Path -LiteralPath $realSettings
    if ($script:RealSettingsExisted) {
        Assert-True $existsNow 'Real user settings file must still exist.'
        $before = $script:RealSettingsStamp
        $after = (Get-Item -LiteralPath $realSettings).LastWriteTimeUtc
        Assert-True ($before -eq $after) 'Real user settings file must not be modified by the isolated run.'
    } else {
        Assert-True (-not $existsNow) 'Isolated run must not create settings in the real %AppData% folder.'
    }
}

function New-DialogFinder {
    param([int]$ProcessId)
    $block = {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $conditions = @(
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, 'Live-Fast Translation'),
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Window)
        )
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.Condition[]]$conditions)
        $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
    return $block.GetNewClosure()
}

$runStamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:ArtifactsDir = Join-Path $PSScriptRoot ('artifacts\e2e_' + $runStamp)
Set-ArtifactsDir -Path $script:ArtifactsDir

try {
    Invoke-Step -Name 'Prepare isolated environment' -Critical {
        Assert-True (Test-Path -LiteralPath $script:ExePath) "Application exe not found: $($script:ExePath)"
        $script:ProfileDir = Join-Path $env:TEMP ('LFT_e2e_profile_' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:ProfileDir -Force | Out-Null
        Assert-True (Test-Path -LiteralPath $script:ProfileDir) 'Temporary profile was not created.'

        $realSettings = Join-Path $env:APPDATA 'LiveFastTranslation\settings.json'
        $script:RealSettingsExisted = Test-Path -LiteralPath $realSettings
        if ($script:RealSettingsExisted) {
            $script:RealSettingsStamp = (Get-Item -LiteralPath $realSettings).LastWriteTimeUtc
        }

        $script:EnvMap = @{
            LFT_DATA_DIR = $script:ProfileDir
            LFT_DISABLE_AUTOSTART = '1'
        }

        $script:Process = Start-AppUnderTest -Path $script:ExePath -Environment $script:EnvMap
        Assert-True ($null -ne $script:Process) 'Process was not started.'
        $script:Window = Wait-Window -ProcessId $script:Process.Id -Title $script:SettingsTitle -Exact -TimeoutMs 30000
        Start-Sleep -Milliseconds 1200
        Set-WindowForeground $script:Window
        $null = Wait-ForElement -AutomationId 'SettingsTitleText' -TimeoutMs 20000
        Assert-True (-not $script:Process.HasExited) 'Application exited during startup.'
        Save-Shot -Name '01_first_run_light'
    }

    Invoke-Step -Name 'First run opens settings in light theme' -Critical {
        Assert-True ($null -ne $script:Window) 'Settings window is missing.'
        $shot = Save-Screenshot -Name '01b_first_run_window' -Element $script:Window
        $script:Screenshots.Add($shot)
        $luminance = Get-ImageLuminance -Path $shot
        Assert-True ($luminance -gt 140) ("Default light theme expected, measured luminance {0:0.0}" -f $luminance)
        $script:LightLuminance = $luminance
    }

    Invoke-Step -Name 'Navigate all six settings sections' {
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
            $anchor = Wait-ForElement -AutomationId $sections[$section] -TimeoutMs 6000
            Assert-True ($null -ne $anchor) "Section $section did not render its anchor control."
            if ($section -eq 'Provider') {
                $language = Wait-ForElement -AutomationId 'TargetLanguageCombo' -TimeoutMs 4000
                Assert-True ([string]$language.Current.Name -eq $script:NativeLanguageLabel) 'Target language combo must be labelled as the native language.'
            }
            if ($section -eq 'Appearance') {
                $autoShow = Wait-ForElement -AutomationId 'AutoShowOverlayCheckBox' -TimeoutMs 4000
                Assert-True ($null -ne $autoShow) 'Auto show overlay checkbox is missing.'
            }
            Save-Shot -Name ("02_nav_" + $section.ToLowerInvariant())
        }
    }

    Invoke-Step -Name 'Search filters sections (latin and russian queries)' {
        Select-Nav -Section 'Provider'
        Set-SearchText -Text 'argos'
        Wait-NavItemCount -Expected 1 | Out-Null
        Assert-True ((Get-NavItemCount) -eq 1) 'Search for "argos" must leave exactly one section.'
        Assert-True ($null -ne (Find-Now -AutomationId 'TestArgosButton')) 'Provider section must stay visible for "argos".'
        Save-Shot -Name '03_search_argos'

        Set-SearchText -Text $script:SearchTheme
        Wait-NavItemCount -Expected 1 | Out-Null
        Assert-True ((Get-NavItemCount) -eq 1) 'Search for the russian word for "theme" must leave exactly one section.'
        Assert-True ($null -ne (Find-Now -AutomationId 'ThemeGallery')) 'Appearance section must become selected after the search.'
        Save-Shot -Name '04_search_theme'

        Set-SearchText -Text 'zzzz-no-match'
        Wait-NavItemCount -Expected 0 | Out-Null
        Assert-True ((Get-NavItemCount) -eq 0) 'A query with no matches must hide every section.'
        Save-Shot -Name '05_search_empty'

        $clear = Wait-ForElement -AutomationId 'ClearSearchButton' -TimeoutMs 3000
        Invoke-Element $clear
        Wait-NavItemCount -Expected 6 | Out-Null
        Assert-True ((Get-NavItemCount) -eq 6) 'Clearing the search must restore all six sections.'
    }

    Invoke-Step -Name 'Argos Translate performs a local test translation' {
        Select-Nav -Section 'Provider'
        $null = Wait-ForElement -AutomationId 'ArgosPythonCommandCombo' -TimeoutMs 6000
        $testButton = Wait-ForElement -AutomationId 'TestArgosButton' -TimeoutMs 6000
        Invoke-Element $testButton

        $finalBadge = Wait-Until -TimeoutMs 150000 -IntervalMs 250 -Condition {
            $badge = Find-Now -AutomationId 'ArgosTestBadgeText'
            if (-not $badge) { return $null }
            $name = [string]$badge.Current.Name
            if ($name -eq $script:WordSuccess -or $name -eq $script:WordError) { return $name }
            return $null
        } -TimeoutMessage 'Argos Translate test did not finish within 150 seconds.'

        $statusElement = Find-Now -AutomationId 'ArgosTestStatusText'
        $statusText = ''
        if ($statusElement) { $statusText = [string]$statusElement.Current.Name }
        Save-Shot -Name '06_argos_test'
        if ($finalBadge -ne $script:WordSuccess) {
            throw ("Argos Translate test failed. Badge='{0}' Status='{1}'" -f $finalBadge, $statusText)
        }
        Assert-True (-not [string]::IsNullOrWhiteSpace($statusText)) 'Argos success status text must not be empty.'
        Assert-True (-not $script:Process.HasExited) 'Application crashed during the Argos test.'
    }

    Invoke-Step -Name 'Switch AI provider to OpenCode (Zen)' {
        $combo = Wait-ForElement -AutomationId 'AiProviderCombo' -TimeoutMs 6000
        Select-ComboItemByName -Element $combo -Name 'OpenCode (Zen)' | Out-Null
        Start-Sleep -Milliseconds 700
        Assert-True (-not $script:Process.HasExited) 'Application crashed after switching the AI provider.'

        $modelCombo = Wait-ForElement -AutomationId 'AiModelCombo' -TimeoutMs 6000
        Set-EditableComboText -Element $modelCombo -Text 'big-pickle'
        Assert-True ((Get-Value $modelCombo) -eq 'big-pickle') 'AI model field did not accept the typed model id.'

        $apiKey = Find-Now -AutomationId 'AiApiKeyBox'
        Assert-True ($null -ne $apiKey) 'OpenCode must still expose an optional API key field.'
        Save-Shot -Name '07_opencode_provider'
    }

    Invoke-Step -Name 'Duplicate hotkey is rejected and reset restores defaults' {
        Select-Nav -Section 'Hotkeys'
        $input = Wait-ForElement -AutomationId 'TranslateHotkeyInput' -TimeoutMs 6000
        Send-VirtualKeysToElement -Window $script:Window -Element $input -VirtualKeys @(0x11, 0x10, 0x51)
        Start-Sleep -Milliseconds 250
        Assert-True ((Get-Value $input) -eq 'Ctrl+Shift+Q') ("Translate hotkey capture failed, value is '{0}'." -f (Get-Value $input))

        $aiInput = Wait-ForElement -AutomationId 'AiHotkeyInput' -TimeoutMs 4000
        Send-VirtualKeysToElement -Window $script:Window -Element $aiInput -VirtualKeys @(0x11, 0x10, 0x51)
        $validation = Wait-ForElement -AutomationId 'HotkeyValidationText' -TimeoutMs 5000
        Start-Sleep -Milliseconds 200
        Assert-True (-not [string]::IsNullOrWhiteSpace([string]$validation.Current.Name)) 'Duplicate hotkey must show a validation message.'
        Save-Shot -Name '08_hotkey_validation'

        $reset = Wait-ForElement -AutomationId 'ResetHotkeysButton' -TimeoutMs 4000
        Invoke-Element $reset
        Start-Sleep -Milliseconds 350
        $validationAfter = Find-Now -AutomationId 'HotkeyValidationText'
        $messageAfter = ''
        if ($validationAfter) { $messageAfter = [string]$validationAfter.Current.Name }
        Assert-True ([string]::IsNullOrWhiteSpace($messageAfter)) 'Validation message must disappear after resetting hotkeys.'
        $translateInput = Wait-ForElement -AutomationId 'TranslateHotkeyInput' -TimeoutMs 3000
        Assert-True ((Get-Value $translateInput) -eq 'Ctrl+Alt+T') 'Translate hotkey must be reset to Ctrl+Alt+T.'
        Save-Shot -Name '09_hotkey_reset'
    }

    Invoke-Step -Name 'Theme gallery applies live preview and minimal mode' {
        Select-Nav -Section 'Appearance'
        $gallery = Wait-ForElement -AutomationId 'ThemeGallery' -TimeoutMs 6000
        $cards = $gallery.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem))
        Assert-True ($cards.Count -eq 64) ("Theme gallery must expose 64 cards, found {0}." -f $cards.Count)

        $nav = Wait-ForElement -AutomationId 'NavSections' -TimeoutMs 4000
        $lightNavShot = Save-Screenshot -Name '10a_appearance_nav_light' -Element $nav
        $script:Screenshots.Add($lightNavShot)
        $lightNavLuminance = Get-ImageLuminance -Path $lightNavShot

        $darkPaper = Wait-ForElement -AutomationId 'dark-paper' -TimeoutMs 5000
        try { Scroll-IntoView $darkPaper } catch { }
        (Get-Pattern $darkPaper ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
        Start-Sleep -Milliseconds 600
        $darkSelected = (Get-Pattern $darkPaper ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected
        Assert-True $darkSelected 'Selecting the dark-paper card must select it.'

        $darkNavShot = Save-Screenshot -Name '10b_appearance_nav_dark' -Element $nav
        $script:Screenshots.Add($darkNavShot)
        $darkNavLuminance = Get-ImageLuminance -Path $darkNavShot
        Write-Host ("       nav luminance: light {0:0.0} -> dark {1:0.0}" -f $lightNavLuminance, $darkNavLuminance) -ForegroundColor DarkGray
        Assert-True ($darkNavLuminance -lt ($lightNavLuminance - 30)) 'Theme selection must apply live: the navigation must become darker.'

        $minimal = Wait-ForElement -AutomationId 'MinimalOverlayCheckBox' -TimeoutMs 4000
        Set-Toggle -Element $minimal -State 'On'
        Start-Sleep -Milliseconds 250

        $opacity = Wait-ForElement -AutomationId 'OverlayOpacitySlider' -TimeoutMs 4000
        Set-RangeValue -Element $opacity -Value 0.9
        Start-Sleep -Milliseconds 300
        $range = Get-Pattern $opacity ([System.Windows.Automation.RangeValuePattern]::Pattern)
        Assert-True ([Math]::Abs($range.Current.Value - 0.9) -lt 0.02) 'Overlay opacity slider did not accept 0.9.'

        $background = Wait-ForElement -AutomationId 'OverlayBackgroundTransparencySlider' -TimeoutMs 4000
        Set-RangeValue -Element $background -Value 0.25
        Start-Sleep -Milliseconds 300
        $backgroundRange = Get-Pattern $background ([System.Windows.Automation.RangeValuePattern]::Pattern)
        Assert-True ([Math]::Abs($backgroundRange.Current.Value - 0.25) -lt 0.02) 'Background transparency slider did not accept 0.25.'
        Save-Shot -Name '10_appearance_dark_selected'
    }

    Invoke-Step -Name 'Save settings with Ctrl+S' {
        $shotsBefore = $script:Screenshots.Count
        $search = Wait-ForElement -AutomationId 'SettingsSearchBox' -TimeoutMs 4000
        Send-VirtualKeysToElement -Window $script:Window -Element $search -VirtualKeys @(0x11, 0x53)
        Wait-WindowGone -ProcessId $script:Process.Id -Title $script:SettingsTitle -TimeoutMs 12000 | Out-Null
        Start-Sleep -Milliseconds 600
        Assert-True (-not $script:Process.HasExited) 'Application must stay alive in the tray after saving.'
        if ($script:Screenshots.Count -eq $shotsBefore) {
            $shot = Save-Screenshot -Name '11_after_save_desktop' -Element ([System.Windows.Automation.AutomationElement]::RootElement)
            $script:Screenshots.Add($shot)
        }
    }

    Invoke-Step -Name 'settings.json contains the saved values' {
        $settings = Read-SettingsJson
        Assert-True ($settings.schemaVersion -eq 1) 'schemaVersion must be 1.'
        Assert-True ($settings.provider.aiProvider -eq 'OpenCode') 'AI provider must be persisted as OpenCode.'
        Assert-True ($settings.provider.profiles.OpenCode.model -eq 'big-pickle') 'OpenCode model must be persisted.'
        Assert-True ($settings.provider.profiles.OpenCode.apiKey -eq '') 'Empty API key must stay empty for OpenCode.'
        Assert-True ($settings.appearance.theme -eq 'dark-paper') 'The last theme in the catalog (dark-paper) must be saved.'
        Assert-True ($settings.appearance.minimalOverlay -eq $true) 'Minimal overlay mode must be saved.'
        Assert-True ([Math]::Abs([double]$settings.appearance.opacity - 0.9) -lt 0.02) 'Saved overlay opacity must be 0.9.'
        Assert-True ([Math]::Abs([double]$settings.appearance.backgroundOpacity - 0.75) -lt 0.02) 'Saved background opacity must be 0.75.'
        Assert-True ($settings.hotkeys.translateSelection -eq 'Ctrl+Alt+T') 'Reset translate hotkey must be persisted.'
        Assert-True ($settings.hotkeys.toggleAiMode -eq 'Ctrl+Alt+A') 'AI hotkey must keep its default.'
        Assert-True ($settings.hotkeys.openSettings -eq 'Ctrl+Alt+S') 'Settings hotkey must keep its default.'
        Assert-SettingsUnchanged
    }

    Invoke-Step -Name 'Second instance opens settings in the running app' {
        $script:SecondProcess = Start-AppUnderTest -Path $script:ExePath -Environment $script:EnvMap
        $exited = $script:SecondProcess.WaitForExit(15000)
        Assert-True $exited 'Second instance must exit after notifying the first instance.'
        Assert-True ($script:SecondProcess.ExitCode -eq 0) ("Second instance exit code must be 0, got {0}" -f $script:SecondProcess.ExitCode)

        $script:Window = Wait-Window -ProcessId $script:Process.Id -Title $script:SettingsTitle -Exact -TimeoutMs 15000
        Start-Sleep -Milliseconds 1200
        Set-WindowForeground $script:Window
        $null = Wait-ForElement -AutomationId 'SettingsTitleText' -TimeoutMs 6000
        $shot = Save-Screenshot -Name '12_second_instance_dark' -Element $script:Window
        $script:Screenshots.Add($shot)
        $luminance = Get-ImageLuminance -Path $shot
        Assert-True ($luminance -lt 120) ("Dark theme expected after restart, measured luminance {0:0.0}" -f $luminance)
        Assert-True ($luminance -lt ($script:LightLuminance - 60)) 'Dark theme must be measurably darker than the initial light theme.'
    }

    Invoke-Step -Name 'Closing with unsaved changes asks for confirmation' {
        Select-Nav -Section 'Advanced'
        $toggle = Wait-ForElement -AutomationId 'TrayNotificationsCheckBox' -TimeoutMs 6000
        $before = Get-ToggleState $toggle
        Set-Toggle -Element $toggle -State $(if ($before -eq 'On') { 'Off' } else { 'On' })
        Start-Sleep -Milliseconds 250

        Send-KeysToWindow -Window $script:Window -Keys '{ESC}'
        $dialog = Wait-Until -TimeoutMs 6000 -IntervalMs 150 -Condition (New-DialogFinder -ProcessId $script:Process.Id) `
            -TimeoutMessage 'Unsaved-changes confirmation dialog did not appear.'
        $dialogMessage = Find-Now -AutomationId 'ConfirmDialogMessage' -Root $dialog
        Assert-True ($null -ne $dialogMessage) 'Confirmation dialog must explain that unsaved changes will be lost.'
        Save-Shot -Name '13_unsaved_changes_prompt' -Element $dialog

        $cancel = Find-Now -AutomationId 'ConfirmDialogCancelButton' -Root $dialog
        Assert-True ($null -ne $cancel) 'Confirmation dialog must offer a safe cancel button.'
        Invoke-Element $cancel
        Wait-Until -TimeoutMs 5000 -IntervalMs 150 -Condition {
            if ($null -eq (& (New-DialogFinder -ProcessId $script:Process.Id))) { $true } else { $false }
        } -TimeoutMessage 'Confirmation dialog did not close after choosing cancel.' | Out-Null

        $stillOpen = Wait-Window -ProcessId $script:Process.Id -Title $script:SettingsTitle -Exact -TimeoutMs 5000
        Assert-True ($null -ne $stillOpen) 'Declining the prompt must keep the settings window open.'
        $script:Window = $stillOpen

        $toggleAgain = Wait-ForElement -AutomationId 'TrayNotificationsCheckBox' -TimeoutMs 4000
        Set-Toggle -Element $toggleAgain -State $before
        Start-Sleep -Milliseconds 250
    }

    Invoke-Step -Name 'Esc closes a clean settings window' {
        Send-KeysToWindow -Window $script:Window -Keys '{ESC}'
        Wait-WindowGone -ProcessId $script:Process.Id -Title $script:SettingsTitle -TimeoutMs 8000 | Out-Null
        Start-Sleep -Milliseconds 600
        Assert-True (-not $script:Process.HasExited) 'Application must keep running in the tray after closing settings.'
        Save-Shot -Name '14_final_tray' -Element ([System.Windows.Automation.AutomationElement]::RootElement)
    }
}
finally {
    Invoke-Step -Name 'Cleanup processes and temporary profile' -Always {
        if ($script:SecondProcess -and -not $script:SecondProcess.HasExited) {
            Stop-Process -Id $script:SecondProcess.Id -Force -ErrorAction SilentlyContinue
        }
        Stop-AppHard -ProcessId $(if ($script:Process) { $script:Process.Id } else { 0 }) -ExePath $script:ExePath
        if (-not $KeepProfile -and $script:ProfileDir -and (Test-Path -LiteralPath $script:ProfileDir)) {
            Remove-Item -LiteralPath $script:ProfileDir -Recurse -Force -ErrorAction SilentlyContinue
            Assert-True (-not (Test-Path -LiteralPath $script:ProfileDir)) 'Temporary profile must be removed from %TEMP%.'
        }
        if ($script:ProfileDir -and $KeepProfile) {
            Write-Host "       profile kept: $($script:ProfileDir)" -ForegroundColor DarkGray
        }
    }

    $finishedAt = Get-Date
    $passed = @($script:Results | Where-Object { $_.status -eq 'pass' }).Count
    $failed = @($script:Results | Where-Object { $_.status -eq 'fail' }).Count
    $skipped = @($script:Results | Where-Object { $_.status -eq 'skipped' }).Count
    $report = [pscustomobject]@{
        startedAt = $script:StartedAt.ToString('o')
        finishedAt = $finishedAt.ToString('o')
        durationSeconds = [Math]::Round(($finishedAt - $script:StartedAt).TotalSeconds, 1)
        exe = $script:ExePath
        artifacts = $script:ArtifactsDir
        profile = $script:ProfileDir
        summary = [pscustomobject]@{
            total = $script:Results.Count
            passed = $passed
            failed = $failed
            skipped = $skipped
        }
        steps = $script:Results
        screenshots = $script:Screenshots
    }
    $json = $report | ConvertTo-Json -Depth 6
    $reportPath = Join-Path $script:ArtifactsDir 'report.json'
    [System.IO.File]::WriteAllText($reportPath, $json, (New-Object System.Text.UTF8Encoding($false)))

    Write-Host ''
    Write-Host '==== E2E RESULT ====' -ForegroundColor White
    Write-Host ("Artifacts: {0}" -f $script:ArtifactsDir) -ForegroundColor DarkGray
    Write-Host ("Report:    {0}" -f $reportPath) -ForegroundColor DarkGray
    Write-Host ("Passed: {0}  Failed: {1}  Skipped: {2}" -f $passed, $failed, $skipped) -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Red' })
    if ($failed -gt 0) {
        foreach ($failure in ($script:Results | Where-Object { $_.status -eq 'fail' })) {
            Write-Host (" - {0}: {1}" -f $failure.name, $failure.detail) -ForegroundColor Red
        }
    }
}

if ($failed -gt 0) { exit 1 }
exit 0
