#Requires -Version 5.1
<#
    Overlay end-to-end check for Live-Fast Translation.

    Starts the app against a throwaway profile with a pre-seeded settings.json,
    opens a deterministic sample window with selected text, presses the global
    translate hotkey and validates the overlay: full (untrimmed) translation,
    anchoring above the input field, no dragging, warm Argos speed, AI toggle,
    minimal mode with a short translation, auto show on selection, refused
    replacement in a read-only field, close button and settings isolation.

    Run:
      powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts/test_overlay.ps1
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
$script:AppProcess = $null
$script:SampleProcess = $null
$script:ShortSampleProcess = $null
$script:ProfileDir = $null
$script:ArtifactsDir = $null
$script:OverlayWindow = $null
$script:SettingsWindow = $null
$script:AutoShownText = ''

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $ExePath) {
    $ExePath = Join-Path $script:RepoRoot 'src\LiveFastTranslation\bin\Debug\net8.0-windows\LiveFastTranslation.exe'
}
$script:ExePath = $ExePath
$script:WindowTitle = 'Live-Fast Translation'
$script:SampleTitle = 'LFT Overlay Sample'
$script:SampleTitleShort = 'LFT Overlay Sample Short'
$script:SampleTitleReadOnly = 'LFT Overlay Sample ReadOnly'
$script:SettingsTitle = 'Live-Fast Translation ' + [char]0x2014 + ' ' + (-join ([char[]]@(
    0x041D, 0x0430, 0x0441, 0x0442, 0x0440, 0x043E, 0x0439, 0x043A, 0x0438)))

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
    if ($null -eq $Element) { $Element = $script:OverlayWindow }
    if ($null -eq $Element) { return }
    $path = Save-Screenshot -Name $Name -Element $Element
    $script:Screenshots.Add($path)
    Write-Host "       screenshot: $path" -ForegroundColor DarkGray
}

function Find-Now {
    param([string]$AutomationId, $Root = $null)
    if ($null -eq $Root) { $Root = $script:OverlayWindow }
    if ($null -eq $Root) { return $null }
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId))
}

function Find-Settings {
    param([string]$AutomationId, [int]$TimeoutMs = 8000)
    return Find-Element -Root $script:SettingsWindow -AutomationId $AutomationId -TimeoutMs $TimeoutMs
}

function Read-SettingsJson {
    param([int]$TimeoutMs = 10000)
    $path = Join-Path $script:ProfileDir 'settings.json'
    $null = Wait-Until -TimeoutMs $TimeoutMs -IntervalMs 150 -Condition {
        (Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Length -gt 2)
    } -TimeoutMessage "settings.json was not written: $path"
    Start-Sleep -Milliseconds 200
    return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json)
}

function Test-Cyrillic {
    param([string]$Text)
    return $Text -match '[\u0400-\u04FF]'
}

function New-SampleExecutable {
    param([Parameter(Mandatory)][string]$Directory)
    $sampleExe = Join-Path $Directory 'LftSample.exe'
    $source = @'
using System;
using System.Drawing;
using System.Windows.Forms;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool readOnly = args.Length > 0 && args[0] == "--read-only";
        bool shortText = args.Length > 0 && args[0] == "--short";
        var form = new Form();
        form.Text = readOnly ? "LFT Overlay Sample ReadOnly"
            : shortText ? "LFT Overlay Sample Short"
            : "LFT Overlay Sample";
        form.Width = 660;
        form.Height = 240;
        form.StartPosition = FormStartPosition.CenterScreen;
        var box = new TextBox();
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.Dock = DockStyle.Fill;
        box.Font = new Font("Segoe UI", 14f);
        box.ReadOnly = readOnly;
        box.Text = readOnly
            ? "This read-only field must refuse the translation replacement attempt."
            : shortText
                ? "Translator"
                : "The quick brown fox jumps over the lazy dog. A warm local translation " +
                    "engine should answer almost instantly after the first request. This deliberately " +
                    "long sample verifies that the overlay grows to fit every sentence instead of " +
                    "trimming the text with an ellipsis. The capsule must stay anchored above the " +
                    "input field and never be draggable, so the user always knows where the " +
                    "translation belongs. Pack my box with five dozen liquor jugs.";
        form.Controls.Add(box);
        form.Shown += delegate { box.Focus(); box.SelectAll(); };
        Application.Run(form);
    }
}
'@
    Add-Type -TypeDefinition $source -OutputAssembly $sampleExe -OutputType WindowsApplication `
        -ReferencedAssemblies 'System.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll'
    if (-not (Test-Path -LiteralPath $sampleExe)) {
        throw "Sample executable was not compiled: $sampleExe"
    }
    return $sampleExe
}

function Invoke-TranslateHotkey {
    param([Parameter(Mandatory)]$Window)
    Set-WindowForeground $Window
    Send-VirtualKeys -VirtualKeys @(0x11, 0x41)
    Start-Sleep -Milliseconds 220
    Send-VirtualKeys -VirtualKeys @(0x11, 0x12, 0x54)
}

function Wait-Translation {
    param([string]$Previous = '', [int]$TimeoutMs = 60000)
    return Wait-Until -TimeoutMs $TimeoutMs -IntervalMs 200 -Condition {
        $element = Find-Now -AutomationId 'OverlayTranslationText'
        if ($null -eq $element) { return $null }
        $text = [string]$element.Current.Name
        if ([string]::IsNullOrWhiteSpace($text)) { return $null }
        if ($Previous -and $text -eq $Previous) { return $null }
        return $element
    } -TimeoutMessage 'The overlay did not show a new translation in time.'
}

function Get-OverlayRect {
    return $script:OverlayWindow.Current.BoundingRectangle
}

function Get-SampleField {
    param($Window = $null)
    if ($null -eq $Window) {
        $Window = Wait-Window -ProcessId $script:SampleProcess.Id -Title $script:SampleTitle -TimeoutMs 5000
    }
    # A multiline WinForms TextBox is exposed as Document, a single-line one as Edit.
    $fieldCondition = [System.Windows.Automation.OrCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Edit),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Document))
    return Wait-Until -TimeoutMs 5000 -IntervalMs 150 -Condition {
        $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fieldCondition)
    } -TimeoutMessage 'The sample input field was not found.'
}

function Select-OverlayLanguage {
    param([Parameter(Mandatory)][string]$Code)
    $combo = Find-Now -AutomationId 'OverlayLanguageCombo'
    Assert-True ($null -ne $combo) 'Overlay language combo is missing.'
    $items = Get-ComboItems -Element $combo
    $target = $null
    foreach ($item in $items) {
        $name = [string]$item.Current.Name
        if ($name -match ('(?i)\b' + [regex]::Escape($Code) + '$')) {
            $target = $item
            break
        }
    }
    Assert-True ($null -ne $target) "Overlay language '$Code' was not found in the combo box."
    Select-Item $target
    Close-Combo $combo
    Start-Sleep -Milliseconds 150
}

function Open-SettingsFromHotkey {
    Send-VirtualKeys -VirtualKeys @(0x11, 0x12, 0x53)
    $script:SettingsWindow = Wait-Window -ProcessId $script:AppProcess.Id -Title $script:SettingsTitle -Exact -TimeoutMs 15000
    Start-Sleep -Milliseconds 900
    Set-WindowForeground $script:SettingsWindow
    $null = Find-Settings -AutomationId 'SettingsTitleText' -TimeoutMs 6000
}

function Select-SettingsSection {
    param([Parameter(Mandatory)][string]$Section)
    $item = Find-Settings -AutomationId $Section -TimeoutMs 6000
    try { Scroll-IntoView $item } catch { }
    (Get-Pattern $item ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 350
}

function Save-Settings {
    $save = Find-Settings -AutomationId 'SaveButton' -TimeoutMs 5000
    Invoke-Element $save
    Wait-WindowGone -ProcessId $script:AppProcess.Id -Title $script:SettingsTitle -TimeoutMs 15000 | Out-Null
    $script:SettingsWindow = $null
    Start-Sleep -Milliseconds 600
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

$runStamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:ArtifactsDir = Join-Path $PSScriptRoot ('artifacts\overlay_' + $runStamp)
Set-ArtifactsDir -Path $script:ArtifactsDir

try {
    Invoke-Step -Name 'Prepare isolated profile and sample application' -Critical {
        Assert-True (Test-Path -LiteralPath $script:ExePath) "Application exe not found: $($script:ExePath)"

        $script:ProfileDir = Join-Path $env:TEMP ('LFT_overlay_profile_' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:ProfileDir -Force | Out-Null

        $realSettings = Join-Path $env:APPDATA 'LiveFastTranslation\settings.json'
        $script:RealSettingsExisted = Test-Path -LiteralPath $realSettings
        if ($script:RealSettingsExisted) {
            $script:RealSettingsStamp = (Get-Item -LiteralPath $realSettings).LastWriteTimeUtc
        }

        # Pre-seed settings so the app starts in the tray instead of opening the settings window.
        $settingsJson = @'
{
  "schemaVersion": 1,
  "provider": { "aiProvider": "OpenAi" },
  "targetLanguage": "ru",
  "hotkeys": { "translateSelection": "Ctrl+Alt+T", "toggleAiMode": "Ctrl+Alt+A", "openSettings": "Ctrl+Alt+S" },
  "replacement": { "strategy": "Win32Message" },
  "aiMode": { "enabled": false },
  "appearance": { "theme": "light-sky", "opacity": 0.96, "fontFamily": "Segoe UI", "fontSize": 13.5, "minimalOverlay": false },
  "general": { "autoStart": false, "startMinimized": false, "saveHistory": false, "historyLimit": 50, "allowClipboardCapture": true, "showTrayNotifications": false, "autoShowOverlay": false }
}
'@
        [System.IO.File]::WriteAllText(
            (Join-Path $script:ProfileDir 'settings.json'),
            $settingsJson,
            (New-Object System.Text.UTF8Encoding($false)))

        $script:SampleExePath = New-SampleExecutable -Directory $script:ProfileDir
        Assert-True (Test-Path -LiteralPath $script:SampleExePath) 'Sample executable is missing.'
    }

    Invoke-Step -Name 'Application starts hidden in the tray' -Critical {
        $script:EnvMap = @{
            LFT_DATA_DIR = $script:ProfileDir
            LFT_DISABLE_AUTOSTART = '1'
        }

        $script:AppProcess = Start-AppUnderTest -Path $script:ExePath -Environment $script:EnvMap
        Assert-True ($null -ne $script:AppProcess) 'Application process was not started.'
        Start-Sleep -Milliseconds 2500
        Assert-True (-not $script:AppProcess.HasExited) 'Application exited during startup.'

        $windows = @(Get-ProcessWindows -ProcessId $script:AppProcess.Id)
        Assert-True ($windows.Count -eq 0) 'A seeded profile must not open any window at startup.'
        Save-Shot -Name '01_tray_only' -Element ([System.Windows.Automation.AutomationElement]::RootElement)
    }

    Invoke-Step -Name 'Sample application shows selected text' -Critical {
        $script:SampleProcess = Start-Process -FilePath $script:SampleExePath -PassThru
        $sampleWindow = Wait-Window -ProcessId $script:SampleProcess.Id -Title $script:SampleTitle -TimeoutMs 15000
        Start-Sleep -Milliseconds 600
        Set-WindowForeground $sampleWindow
        Assert-True ($null -ne $sampleWindow) 'Sample window was not found.'
        Save-Shot -Name '02_sample_selected' -Element $sampleWindow
    }

    Invoke-Step -Name 'Global hotkey shows the full translation anchored above the field' -Critical {
        $sampleWindow = Wait-Window -ProcessId $script:SampleProcess.Id -Title $script:SampleTitle -TimeoutMs 4000
        Invoke-TranslateHotkey -Window $sampleWindow

        $script:OverlayWindow = Wait-Window -ProcessId $script:AppProcess.Id -Title $script:WindowTitle -Exact -TimeoutMs 20000
        Assert-True ($null -ne $script:OverlayWindow) 'Overlay window did not appear.'

        $textElement = Wait-Translation -TimeoutMs 60000
        $script:Translation = [string]$textElement.Current.Name
        Write-Host "       translation: $($script:Translation.Length) chars" -ForegroundColor DarkGray
        Assert-True (-not [string]::IsNullOrWhiteSpace($script:Translation)) 'Overlay translation text must not be empty.'
        Assert-True (Test-Cyrillic $script:Translation) 'Russian translation must contain Cyrillic characters.'
        Assert-True ($script:Translation.Length -ge 100) 'The long sample must produce a long translation, not a trimmed fragment.'

        $badge = Find-Now -AutomationId 'OverlaySourceBadge'
        Assert-True ($null -ne $badge) 'Overlay source language badge is missing.'

        $field = Get-SampleField
        $fieldRect = $field.Current.BoundingRectangle
        $overlayRect = Get-OverlayRect
        $capsuleTop = $overlayRect.Top + 10
        $capsuleBottom = $overlayRect.Bottom - 10
        $capsuleLeft = $overlayRect.Left + 10
        $capsuleHeight = $overlayRect.Height - 20
        $capsuleWidth = $overlayRect.Width - 20

        Write-Host ("       field: {0:0},{1:0} {2:0}x{3:0}  capsule: {4:0},{5:0} {6:0}x{7:0}" -f `
            $fieldRect.X, $fieldRect.Y, $fieldRect.Width, $fieldRect.Height,
            $capsuleLeft, $capsuleTop, $capsuleWidth, $capsuleHeight) -ForegroundColor DarkGray

        Assert-True ($capsuleBottom -le ($fieldRect.Top + 2)) 'The capsule must sit above the input field.'
        Assert-True ($capsuleBottom -ge ($fieldRect.Top - 40)) 'The capsule must stay close to the input field.'
        Assert-True ([Math]::Abs($capsuleLeft - $fieldRect.Left) -le 24) 'The capsule left edge must align with the input field.'
        Assert-True ($capsuleHeight -ge 64) 'A long translation must grow the capsule instead of clipping it.'

        $textRect = $textElement.Current.BoundingRectangle
        Write-Host ("       text: {0:0},{1:0} {2:0}x{3:0}" -f $textRect.X, $textRect.Y, $textRect.Width, $textRect.Height) -ForegroundColor DarkGray
        Assert-True ($textRect.Bottom -le ($capsuleBottom + 1)) 'The translation text must fit inside the capsule without clipping.'
        Assert-True ($textRect.Height -ge 60) 'The translation must occupy several lines instead of being squeezed into one.'

        $screenWidth = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Width
        Assert-True ($capsuleWidth -le ($screenWidth * 0.7)) 'The capsule must not become wider than the screen limit.'

        $script:OverlayRectAtAnchor = $overlayRect
        $script:FieldRect = $fieldRect
        Save-Shot -Name '03_overlay_translated'
    }

    Invoke-Step -Name 'Overlay cannot be dragged away from the input field' {
        $badge = Find-Now -AutomationId 'OverlaySourceBadge'
        Assert-True ($null -ne $badge) 'Overlay source language badge is missing before the drag test.'
        $before = Get-OverlayRect
        Invoke-MouseDrag -Element $badge -OffsetX 90 -OffsetY 50
        Start-Sleep -Milliseconds 400
        $after = Get-OverlayRect

        Assert-True ([Math]::Abs($after.X - $before.X) -le 2) 'The overlay must not move horizontally when dragged.'
        Assert-True ([Math]::Abs($after.Y - $before.Y) -le 2) 'The overlay must not move vertically when dragged.'
        Assert-True ($null -ne (Find-Now -AutomationId 'OverlayTranslationText')) 'The overlay must stay visible after a drag attempt.'
        Save-Shot -Name '04_overlay_after_drag'
    }

    Invoke-Step -Name 'Warm Argos translates again in under ten seconds' {
        $first = $script:Translation
        $total = [System.Diagnostics.Stopwatch]::StartNew()
        Select-OverlayLanguage -Code 'DE'
        $german = Wait-Translation -Previous $first -TimeoutMs 9000
        $total.Stop()
        $germanText = [string]$german.Current.Name
        Assert-True (-not [string]::IsNullOrWhiteSpace($germanText)) 'German translation must not be empty.'
        Write-Host ("       german retranslate: {0} ms" -f $total.ElapsedMilliseconds) -ForegroundColor DarkGray
        # Switching the target language loads another Argos package (several seconds on a cold
        # model), so this checks that the persistent process avoids a full Python restart.
        Assert-True ($total.ElapsedMilliseconds -lt 10000) 'The warm Argos process must answer in under ten seconds.'

        $settingsAfterGerman = Read-SettingsJson
        Assert-True ($settingsAfterGerman.targetLanguage -eq 'ru') 'Choosing a language in the overlay must not overwrite the native language.'

        $back = [System.Diagnostics.Stopwatch]::StartNew()
        Select-OverlayLanguage -Code 'RU'
        $russian = Wait-Translation -Previous $germanText -TimeoutMs 9000
        $back.Stop()
        $russianText = [string]$russian.Current.Name
        Write-Host ("       russian retranslate: {0} ms" -f $back.ElapsedMilliseconds) -ForegroundColor DarkGray
        Assert-True (Test-Cyrillic $russianText) 'Switching back to Russian must show a Cyrillic translation.'
        Assert-True ($back.ElapsedMilliseconds -lt 10000) 'Repeated Argos translations must stay fast.'

        $settings = Read-SettingsJson
        Assert-True ($settings.targetLanguage -eq 'ru') 'The native language must stay ru after overlay retranslations.'
        Save-Shot -Name '05_overlay_retranslated'
    }

    Invoke-Step -Name 'AI toggle in the overlay persists to settings.json' {
        $toggle = Find-Now -AutomationId 'OverlayAiToggle'
        Assert-True ($null -ne $toggle) 'Overlay AI toggle is missing.'
        $pattern = Get-Pattern $toggle ([System.Windows.Automation.TogglePattern]::Pattern)
        Assert-True ($pattern.Current.ToggleState -eq 'Off') 'AI mode must start disabled in the seeded profile.'

        $pattern.Toggle()
        $enabled = Wait-Until -TimeoutMs 10000 -IntervalMs 200 -Condition {
            $settings = Read-SettingsJson
            if ($settings.aiMode.enabled -eq $true) { return $true }
            return $false
        } -TimeoutMessage 'AI toggle did not persist aiMode.enabled=true.'
        Assert-True $enabled 'AI toggle must persist aiMode.enabled=true.'
        Save-Shot -Name '06_overlay_ai_on'

        $pattern.Toggle()
        $null = Wait-Until -TimeoutMs 10000 -IntervalMs 200 -Condition {
            $settings = Read-SettingsJson
            if ($settings.aiMode.enabled -eq $false) { return $true }
            return $false
        } -TimeoutMessage 'AI toggle did not persist aiMode.enabled=false.'
    }

    Invoke-Step -Name 'Close button hides the overlay and keeps the app running' {
        $close = Find-Now -AutomationId 'OverlayCloseButton'
        Assert-True ($null -ne $close) 'Overlay close button is missing.'
        Invoke-Element $close
        Wait-Until -TimeoutMs 8000 -IntervalMs 200 -Condition {
            if ($null -eq (Find-Now -AutomationId 'OverlayTranslationText')) { $true } else { $false }
        } -TimeoutMessage 'Overlay did not hide after pressing its close button.' | Out-Null

        Start-Sleep -Milliseconds 400
        Assert-True (-not $script:AppProcess.HasExited) 'Application must keep running in the tray after closing the overlay.'
        Assert-True (@(Get-ProcessWindows -ProcessId $script:AppProcess.Id).Count -eq 0) 'No application window may stay visible.'
        Save-Shot -Name '07_overlay_closed' -Element ([System.Windows.Automation.AutomationElement]::RootElement)
    }

    Invoke-Step -Name 'Theme gallery offers all 64 themes' {
        Open-SettingsFromHotkey
        Select-SettingsSection -Section 'Appearance'
        $gallery = Find-Settings -AutomationId 'ThemeGallery' -TimeoutMs 8000
        $items = $gallery.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::ListItem))
        Write-Host "       theme cards: $($items.Count)" -ForegroundColor DarkGray
        Assert-True ($items.Count -eq 64) 'The theme gallery must expose all 64 themes.'
        Assert-True ($null -ne (Find-Settings -AutomationId 'MinimalOverlayCheckBox' -TimeoutMs 4000)) 'Minimal overlay checkbox is missing.'
        Assert-True ($null -ne (Find-Settings -AutomationId 'AutoShowOverlayCheckBox' -TimeoutMs 4000)) 'Auto show overlay checkbox is missing.'
        Save-Shot -Name '08_theme_gallery' -Element $script:SettingsWindow
    }

    Invoke-Step -Name 'Minimal mode hides every overlay control but keeps the text' {
        $minimal = Find-Settings -AutomationId 'MinimalOverlayCheckBox' -TimeoutMs 4000
        Set-Toggle -Element $minimal -State 'On'
        Start-Sleep -Milliseconds 250
        Save-Settings

        $settings = Read-SettingsJson
        Assert-True ($settings.appearance.minimalOverlay -eq $true) 'Minimal overlay flag must be saved.'

        $sampleWindow = Wait-Window -ProcessId $script:SampleProcess.Id -Title $script:SampleTitle -TimeoutMs 5000
        Invoke-TranslateHotkey -Window $sampleWindow
        $script:OverlayWindow = Wait-Window -ProcessId $script:AppProcess.Id -Title $script:WindowTitle -Exact -TimeoutMs 15000
        $textElement = Wait-Translation -TimeoutMs 20000
        $minimalText = [string]$textElement.Current.Name
        Assert-True (Test-Cyrillic $minimalText) 'Minimal mode must still translate the selection.'
        Assert-True ($minimalText.Length -ge 100) 'Minimal mode must show the complete translation.'

        foreach ($controlId in @('OverlaySourceBadge', 'OverlayLanguageCombo', 'OverlayAiToggle', 'OverlaySettingsButton', 'OverlayCloseButton')) {
            Assert-True ($null -eq (Find-Now -AutomationId $controlId)) "Minimal mode must hide $controlId."
        }

        $rect = Get-OverlayRect
        $minimalTextRect = $textElement.Current.BoundingRectangle
        Assert-True ($minimalTextRect.Height -ge 40) 'Minimal mode must show several lines of the full translation.'
        Assert-True ($minimalTextRect.Bottom -le ($rect.Bottom - 4)) 'Minimal mode must not clip the translation.'
        Save-Shot -Name '09_overlay_minimal'

        # A short translation must shrink the capsule instead of leaving an empty stretch.
        $script:ShortSampleProcess = Start-Process -FilePath $script:SampleExePath -ArgumentList '--short' -PassThru
        $shortWindow = Wait-Window -ProcessId $script:ShortSampleProcess.Id -Title $script:SampleTitleShort -Exact -TimeoutMs 15000
        Start-Sleep -Milliseconds 600
        Invoke-TranslateHotkey -Window $shortWindow
        $shortElement = Wait-Translation -Previous $minimalText -TimeoutMs 20000
        $shortText = [string]$shortElement.Current.Name
        Assert-True (-not [string]::IsNullOrWhiteSpace($shortText)) 'A short translation must not be empty.'
        $shortCapsuleWidth = Wait-Until -TimeoutMs 5000 -IntervalMs 150 -Condition {
            $width = (Get-OverlayRect).Width - 20
            if ($width -lt 180) { return $width }
            return $null
        } -TimeoutMessage 'Minimal mode must shrink the capsule to fit a short translation.'
        Write-Host ("       short capsule: {0:0} px, text: '{1}'" -f $shortCapsuleWidth, $shortText) -ForegroundColor DarkGray
        Save-Shot -Name '09b_overlay_minimal_short'
        Stop-Process -Id $script:ShortSampleProcess.Id -Force -ErrorAction SilentlyContinue
        $script:ShortSampleProcess = $null

        Invoke-PointRightClick -Element $shortElement
        Wait-Until -TimeoutMs 8000 -IntervalMs 200 -Condition {
            if ($null -eq (Find-Now -AutomationId 'OverlayTranslationText')) { $true } else { $false }
        } -TimeoutMessage 'Right click did not hide the minimal overlay.' | Out-Null

        Open-SettingsFromHotkey
        Select-SettingsSection -Section 'Appearance'
        $minimalAgain = Find-Settings -AutomationId 'MinimalOverlayCheckBox' -TimeoutMs 5000
        Set-Toggle -Element $minimalAgain -State 'Off'
        Start-Sleep -Milliseconds 250
        Save-Settings

        $settingsAfter = Read-SettingsJson
        Assert-True ($settingsAfter.appearance.minimalOverlay -eq $false) 'Minimal overlay flag must be switched off again.'
        Save-Shot -Name '10_minimal_disabled' -Element ([System.Windows.Automation.AutomationElement]::RootElement)
    }

    Invoke-Step -Name 'Auto show translates a selection without a hotkey' {
        Open-SettingsFromHotkey
        Select-SettingsSection -Section 'Appearance'
        $autoShow = Find-Settings -AutomationId 'AutoShowOverlayCheckBox' -TimeoutMs 5000
        Set-Toggle -Element $autoShow -State 'On'
        Start-Sleep -Milliseconds 250
        Save-Settings

        $settings = Read-SettingsJson
        Assert-True ($settings.general.autoShowOverlay -eq $true) 'Auto show overlay flag must be saved.'

        $sampleWindow = Wait-Window -ProcessId $script:SampleProcess.Id -Title $script:SampleTitle -TimeoutMs 5000
        Set-WindowForeground $sampleWindow
        Send-VirtualKeys -VirtualKeys @(0x11, 0x41)
        $autoElement = Wait-Translation -TimeoutMs 30000
        $script:AutoShownText = [string]$autoElement.Current.Name
        Assert-True (-not [string]::IsNullOrWhiteSpace($script:AutoShownText)) 'Auto show must translate the selection without a hotkey.'
        Assert-True ($null -ne (Find-Now -AutomationId 'OverlayTranslationText')) 'Auto show must make the overlay visible.'
        Save-Shot -Name '11_overlay_auto_shown'

        Open-SettingsFromHotkey
        Select-SettingsSection -Section 'Appearance'
        $autoShowOff = Find-Settings -AutomationId 'AutoShowOverlayCheckBox' -TimeoutMs 5000
        Set-Toggle -Element $autoShowOff -State 'Off'
        Start-Sleep -Milliseconds 250
        Save-Settings

        $settingsOff = Read-SettingsJson
        Assert-True ($settingsOff.general.autoShowOverlay -eq $false) 'Auto show overlay flag must be switched off again.'
    }

    Invoke-Step -Name 'Read-only field is not replaced and says so' {
        $readOnlyProcess = Start-Process -FilePath $script:SampleExePath -ArgumentList '--read-only' -PassThru
        try {
            $readOnlyWindow = Wait-Window -ProcessId $readOnlyProcess.Id -Title $script:SampleTitleReadOnly -Exact -TimeoutMs 15000
            Start-Sleep -Milliseconds 600
            Set-WindowForeground $readOnlyWindow

            Invoke-TranslateHotkey -Window $readOnlyWindow
            $script:OverlayWindow = Wait-Window -ProcessId $script:AppProcess.Id -Title $script:WindowTitle -Exact -TimeoutMs 15000
            $translated = Wait-Translation -Previous $script:AutoShownText -TimeoutMs 30000
            Assert-True (-not [string]::IsNullOrWhiteSpace([string]$translated.Current.Name)) 'The read-only sample must translate.'

            $readOnlyField = Get-SampleField -Window $readOnlyWindow
            $before = (Get-Text -Element $readOnlyField).Trim()
            $notReplaced = -join ([char[]]@(
                0x041D, 0x0435, 0x0020, 0x0437, 0x0430, 0x043C, 0x0435, 0x043D, 0x0435, 0x043D, 0x043E))

            Invoke-PointClick -Element $translated
            $errorElement = Wait-Until -TimeoutMs 12000 -IntervalMs 200 -Condition {
                $element = Find-Now -AutomationId 'OverlayErrorText'
                if ($null -eq $element) { return $null }
                $name = [string]$element.Current.Name
                if ([string]::IsNullOrWhiteSpace($name)) { return $null }
                return $element
            } -TimeoutMessage 'The overlay did not report the refused replacement.'
            $errorText = [string]$errorElement.Current.Name
            Write-Host "       replacement error: $errorText" -ForegroundColor DarkGray
            Assert-True ($errorText.StartsWith($notReplaced)) 'A refused replacement must start with the not-replaced label.'
            Assert-True (((Get-Text -Element $readOnlyField).Trim()) -eq $before) 'A read-only field must keep its text after a refused replacement.'
            Save-Shot -Name '12_replacement_refused'
        }
        finally {
            if ($readOnlyProcess -and -not $readOnlyProcess.HasExited) {
                Stop-Process -Id $readOnlyProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Invoke-Step -Name 'Isolated profile did not touch the real user settings' {
        $settings = Read-SettingsJson
        Assert-True ($settings.targetLanguage -eq 'ru') 'Seeded target language must stay ru.'
        Assert-SettingsUnchanged
    }
}
finally {
    Invoke-Step -Name 'Cleanup processes and temporary profile' -Always {
        if ($script:SampleProcess -and -not $script:SampleProcess.HasExited) {
            Stop-Process -Id $script:SampleProcess.Id -Force -ErrorAction SilentlyContinue
        }
        if ($script:ShortSampleProcess -and -not $script:ShortSampleProcess.HasExited) {
            Stop-Process -Id $script:ShortSampleProcess.Id -Force -ErrorAction SilentlyContinue
        }
        Stop-AppHard -ProcessId $(if ($script:AppProcess) { $script:AppProcess.Id } else { 0 }) -ExePath $script:ExePath

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
    Write-Host '==== OVERLAY RESULT ====' -ForegroundColor White
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
