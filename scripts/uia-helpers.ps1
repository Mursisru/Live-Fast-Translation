#Requires -Version 5.1
# Shared UI Automation helpers for Live-Fast Translation checks.
# ASCII-only on purpose: Windows PowerShell 5.1 then never depends on a BOM.

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not ('LftUia.Native' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
namespace LftUia {
  public static class Native {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  }
}
"@
}
[LftUia.Native]::SetProcessDPIAware() | Out-Null

$script:ArtifactsDir = $null

function Set-ArtifactsDir {
    param([Parameter(Mandatory)][string]$Path)
    $script:ArtifactsDir = $Path
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

# ---------------------------------------------------------------- waiting ---
function Wait-Until {
    param(
        [Parameter(Mandatory)][scriptblock]$Condition,
        [int]$TimeoutMs = 10000,
        [int]$IntervalMs = 100,
        [string]$TimeoutMessage = 'Timed out while waiting for a condition'
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $result = & $Condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds $IntervalMs
    } while ($sw.ElapsedMilliseconds -lt $TimeoutMs)
    throw $TimeoutMessage
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "ASSERT FAILED: $Message" }
}

# ------------------------------------------------------------ application ---
function Start-AppUnderTest {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Arguments,
        [hashtable]$Environment,
        [int]$TimeoutMs = 20000
    )
    $exe = (Resolve-Path -LiteralPath $Path).Path
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.WorkingDirectory = (Split-Path -Parent $exe)
    $psi.UseShellExecute = $false
    if ($Arguments) { $psi.Arguments = $Arguments }
    if ($Environment) {
        foreach ($key in $Environment.Keys) {
            $psi.EnvironmentVariables[[string]$key] = [string]$Environment[$key]
        }
    }
    $process = [System.Diagnostics.Process]::Start($psi)
    try { $process.WaitForInputIdle($TimeoutMs) | Out-Null } catch { }
    return $process
}

function Stop-AppHard {
    param([int]$ProcessId, [string]$ExePath, [int]$GraceMs = 3000)
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($process) {
        try { $process.CloseMainWindow() | Out-Null } catch { }
        if (-not $process.WaitForExit($GraceMs)) {
            Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
    if ($ExePath) {
        $full = (Resolve-Path -LiteralPath $ExePath -ErrorAction SilentlyContinue).Path
        if ($full) {
            foreach ($candidate in (Get-Process -ErrorAction SilentlyContinue)) {
                try {
                    if ($candidate.Path -and ($candidate.Path -ieq $full)) {
                        Stop-Process -Id $candidate.Id -Force -ErrorAction SilentlyContinue
                    }
                } catch { }
            }
        }
    }
}

function Get-ProcessWindows {
    param([Parameter(Mandatory)][int]$ProcessId)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $conditions = @(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)
    )
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.Condition[]]$conditions)
    return $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
}

function Wait-Window {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [string]$Title,
        [switch]$Exact,
        [int]$TimeoutMs = 25000
    )
    $condition = {
        foreach ($window in (Get-ProcessWindows -ProcessId $ProcessId)) {
            try {
                $name = [string]$window.Current.Name
                if (-not $Title) { return $window }
                if ($Exact) {
                    if ($name -eq $Title) { return $window }
                } elseif ($name -like "*$Title*") {
                    return $window
                }
            } catch { }
        }
        $null
    }
    return Wait-Until -TimeoutMs $TimeoutMs -Condition $condition -TimeoutMessage (
        "Window was not found. PID=$ProcessId Title='$Title'")
}

function Wait-WindowGone {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [string]$Title,
        [int]$TimeoutMs = 15000
    )
    $condition = {
        $found = $null
        foreach ($window in (Get-ProcessWindows -ProcessId $ProcessId)) {
            try {
                if (-not $Title -or [string]$window.Current.Name -like "*$Title*") {
                    $found = $window
                    break
                }
            } catch { }
        }
        return (-not $found)
    }
    return Wait-Until -TimeoutMs $TimeoutMs -Condition $condition -TimeoutMessage (
        "Window did not close. PID=$ProcessId Title='$Title'")
}

function Wait-ProcessExit {
    param(
        [Parameter(Mandatory)]$Process,
        [int]$TimeoutMs = 10000,
        [string]$Message = 'Process did not exit'
    )
    if (-not $Process.WaitForExit($TimeoutMs)) { throw $Message }
    return $true
}

# --------------------------------------------------------------- elements ---
function Find-Element {
    param(
        [Parameter(Mandatory)]$Root,
        [string]$AutomationId,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType,
        [System.Windows.Automation.TreeScope]$Scope = [System.Windows.Automation.TreeScope]::Descendants,
        [int]$TimeoutMs = 10000
    )
    $conditions = @()
    if ($AutomationId) {
        $conditions += [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $AutomationId)
    }
    if ($Name) {
        $conditions += [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    }
    if ($ControlType) {
        $conditions += [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType)
    }
    if ($conditions.Count -eq 0) {
        throw 'Find-Element needs AutomationId, Name or ControlType'
    }
    $elementCondition = if ($conditions.Count -eq 1) {
        $conditions[0]
    } else {
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.Condition[]]$conditions)
    }

    try {
        return Wait-Until -TimeoutMs $TimeoutMs -Condition {
            $Root.FindFirst($Scope, $elementCondition)
        }
    } catch {
        throw "Element not found in ${TimeoutMs}ms: Id='$AutomationId' Name='$Name' Type='$ControlType'"
    }
}

function Try-Find-Element {
    param(
        [Parameter(Mandatory)]$Root,
        [string]$AutomationId,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType,
        [System.Windows.Automation.TreeScope]$Scope = [System.Windows.Automation.TreeScope]::Descendants
    )
    try {
        return Find-Element -Root $Root -AutomationId $AutomationId -Name $Name `
            -ControlType $ControlType -Scope $Scope -TimeoutMs 1200
    } catch {
        return $null
    }
}

function Get-Pattern {
    param(
        [Parameter(Mandatory)]$Element,
        [Parameter(Mandatory)][System.Windows.Automation.AutomationPattern]$Pattern
    )
    $value = $null
    if ($Element.TryGetCurrentPattern($Pattern, [ref]$value)) { return $value }
    throw "Pattern $($Pattern.ProgrammaticName) is not supported by '$($Element.Current.Name)'"
}

function Invoke-Element {
    param([Parameter(Mandatory)]$Element)
    (Get-Pattern $Element ([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}

function Focus-Element {
    param([Parameter(Mandatory)]$Element)
    try { $Element.SetFocus() } catch { }
    Start-Sleep -Milliseconds 80
}

function Set-Toggle {
    param(
        [Parameter(Mandatory)]$Element,
        [ValidateSet('On', 'Off')][string]$State
    )
    $pattern = Get-Pattern $Element ([System.Windows.Automation.TogglePattern]::Pattern)
    if (-not $State) { $pattern.Toggle(); return }
    $guard = 0
    while ($pattern.Current.ToggleState.ToString() -ne $State -and $guard -lt 4) {
        $pattern.Toggle()
        Start-Sleep -Milliseconds 80
        $guard++
    }
    Assert-True ($pattern.Current.ToggleState.ToString() -eq $State) "Toggle did not reach $State"
}

function Get-ToggleState {
    param([Parameter(Mandatory)]$Element)
    (Get-Pattern $Element ([System.Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState.ToString()
}

function Set-Value {
    param(
        [Parameter(Mandatory)]$Element,
        [AllowEmptyString()][string]$Value
    )
    $pattern = Get-Pattern $Element ([System.Windows.Automation.ValuePattern]::Pattern)
    Focus-Element $Element
    $pattern.SetValue($Value)
}

function Get-Value {
    param([Parameter(Mandatory)]$Element)
    (Get-Pattern $Element ([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value
}

function Get-Text {
    param([Parameter(Mandatory)]$Element)
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref]$pattern)) {
        return $pattern.DocumentRange.GetText(-1)
    }
    return [string]$Element.Current.Name
}

function Select-Item {
    param([Parameter(Mandatory)]$Element)
    (Get-Pattern $Element ([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
}

function Set-Expanded {
    param(
        [Parameter(Mandatory)]$Element,
        [ValidateSet('Expand', 'Collapse')][string]$Action = 'Expand'
    )
    $pattern = Get-Pattern $Element ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($Action -eq 'Expand') { $pattern.Expand() } else { $pattern.Collapse() }
}

function Set-RangeValue {
    param(
        [Parameter(Mandatory)]$Element,
        [double]$Value
    )
    (Get-Pattern $Element ([System.Windows.Automation.RangeValuePattern]::Pattern)).SetValue($Value)
}

function Scroll-IntoView {
    param([Parameter(Mandatory)]$Element)
    (Get-Pattern $Element ([System.Windows.Automation.ScrollItemPattern]::Pattern)).ScrollIntoView()
}

function Get-ComboItems {
    param([Parameter(Mandatory)]$Element, [int]$TimeoutMs = 5000)
    Set-Expanded $Element -Action Expand
    $listCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::List)
    $list = $null
    try {
        $list = Wait-Until -TimeoutMs $TimeoutMs -Condition {
            $Element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCondition)
        } -TimeoutMessage 'ComboBox drop-down list did not appear'
    } catch {
        $list = $null
    }
    $root = if ($list) { $list } else { $Element }
    $itemCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    return $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
}

function Close-Combo {
    param([Parameter(Mandatory)]$Element)
    try { Set-Expanded $Element -Action Collapse } catch { }
}

function Select-ComboItemByName {
    param(
        [Parameter(Mandatory)]$Element,
        [Parameter(Mandatory)][string]$Name,
        [int]$TimeoutMs = 6000
    )
    Set-Expanded $Element -Action Expand
    $itemCondition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $Name))
    $item = Wait-Until -TimeoutMs $TimeoutMs -Condition {
        $Element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    } -TimeoutMessage "ComboBox item '$Name' was not found"
    Select-Item $item
    Close-Combo $Element
    return $item
}

function Get-ComboSelectedName {
    param([Parameter(Mandatory)]$Element)
    $pattern = Get-Pattern $Element ([System.Windows.Automation.SelectionPattern]::Pattern)
    $selection = $pattern.GetCurrentSelection()
    if ($selection.Count -eq 0) { return '' }
    return [string]$selection[0].Current.Name
}

# ------------------------------------------------------------ input/keys ----
function Set-WindowForeground {
    param([Parameter(Mandatory)]$Window)
    $handle = [IntPtr][int]$Window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { return }
    [LftUia.Native]::ShowWindow($handle, 9) | Out-Null
    [LftUia.Native]::SetForegroundWindow($handle) | Out-Null
    try { $Window.SetFocus() } catch { }
    Start-Sleep -Milliseconds 180
}

function Send-KeysToWindow {
    param(
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)][string]$Keys,
        [int]$DelayMs = 220,
        [switch]$SkipForeground
    )
    if (-not $SkipForeground) { Set-WindowForeground $Window }
    Start-Sleep -Milliseconds $DelayMs
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

function Send-KeysToElement {
    param(
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)]$Element,
        [Parameter(Mandatory)][string]$Keys,
        [int]$DelayMs = 180
    )
    Set-WindowForeground $Window
    Focus-Element $Element
    Start-Sleep -Milliseconds $DelayMs
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

# SendKeys maps characters through the active keyboard layout, so latin letters
# are silently dropped when a russian layout is active. Virtual-key input is
# layout independent: 0x11=Ctrl, 0x12=Alt, 0x10=Shift, 0x5B=Win.
function Send-VirtualKeys {
    param(
        [Parameter(Mandatory)][int[]]$VirtualKeys,
        [int]$HoldMs = 30,
        [int]$GapMs = 20
    )
    foreach ($vk in $VirtualKeys) {
        [LftUia.Native]::keybd_event([byte]$vk, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds $HoldMs
    }
    for ($i = $VirtualKeys.Count - 1; $i -ge 0; $i--) {
        [LftUia.Native]::keybd_event([byte]$VirtualKeys[$i], 0, 2, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds $GapMs
    }
}

function Send-VirtualKeysToElement {
    param(
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)]$Element,
        [Parameter(Mandatory)][int[]]$VirtualKeys,
        [int]$DelayMs = 180
    )
    Set-WindowForeground $Window
    Focus-Element $Element
    Start-Sleep -Milliseconds $DelayMs
    Send-VirtualKeys -VirtualKeys $VirtualKeys
}

function Invoke-PointClick {
    param([Parameter(Mandatory)]$Element)
    $point = $Element.GetClickablePoint()
    [LftUia.Native]::SetCursorPos([int]$point.X, [int]$point.Y) | Out-Null
    Start-Sleep -Milliseconds 60
    [LftUia.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [LftUia.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-PointRightClick {
    param([Parameter(Mandatory)]$Element)
    $point = $Element.GetClickablePoint()
    [LftUia.Native]::SetCursorPos([int]$point.X, [int]$point.Y) | Out-Null
    Start-Sleep -Milliseconds 60
    [LftUia.Native]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
    [LftUia.Native]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
}

# Presses the left button on an element and drags by a screen offset. Used to prove
# that the overlay cannot be moved away from its anchor.
function Invoke-MouseDrag {
    param(
        [Parameter(Mandatory)]$Element,
        [int]$OffsetX = 80,
        [int]$OffsetY = 40,
        [int]$Steps = 4
    )
    $point = $Element.GetClickablePoint()
    $startX = [int]$point.X
    $startY = [int]$point.Y
    [LftUia.Native]::SetCursorPos($startX, $startY) | Out-Null
    Start-Sleep -Milliseconds 80
    [LftUia.Native]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    for ($step = 1; $step -le $Steps; $step++) {
        [LftUia.Native]::SetCursorPos(
            $startX + [int]($OffsetX * $step / $Steps),
            $startY + [int]($OffsetY * $step / $Steps)) | Out-Null
        Start-Sleep -Milliseconds 60
    }
    [LftUia.Native]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
}

# ------------------------------------------------------------- screenshot ---
function New-ShotPath {
    param([string]$Name)
    if (-not $script:ArtifactsDir) { throw 'Call Set-ArtifactsDir first' }
    $safe = ($Name -replace '[^\w\-\.]', '_')
    return (Join-Path $script:ArtifactsDir ('{0}_{1:yyyyMMdd_HHmmss_fff}.png' -f $safe, (Get-Date)))
}

function Save-Screenshot {
    param(
        [string]$Name = 'shot',
        $Element
    )
    if ($Element) {
        $rect = $Element.Current.BoundingRectangle
        if ($rect.IsEmpty -or $rect.Width -le 0 -or $rect.Height -le 0) {
            throw 'Element has no visible area'
        }
        $x = [int]$rect.X
        $y = [int]$rect.Y
        $w = [int][Math]::Ceiling($rect.Width)
        $h = [int][Math]::Ceiling($rect.Height)
    } else {
        $virtual = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $x = $virtual.X
        $y = $virtual.Y
        $w = $virtual.Width
        $h = $virtual.Height
    }

    $file = New-ShotPath $Name
    $bitmap = [System.Drawing.Bitmap]::new($w, $h)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($x, $y, 0, 0, [System.Drawing.Size]::new($w, $h))
        $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    return $file
}

function Get-WindowRect {
    param([Parameter(Mandatory)]$Window)
    $handle = [IntPtr][int]$Window.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { throw 'Window has no native handle' }
    $rect = New-Object 'LftUia.Native+RECT'
    [LftUia.Native]::GetWindowRect($handle, [ref]$rect) | Out-Null
    return $rect
}

# --------------------------------------------------------------- utilities ---
function Dump-UiTree {
    param(
        [Parameter(Mandatory)]$Element,
        [int]$Depth = 0,
        [int]$MaxDepth = 12
    )
    $current = $Element.Current
    $type = $current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
    $offscreen = if ($current.IsOffscreen) { ' [OFFSCREEN]' } else { '' }
    '{0}{1} id="{2}" name="{3}"{4}' -f ('  ' * $Depth), $type, $current.AutomationId, $current.Name, $offscreen
    if ($Depth -ge $MaxDepth) { return }
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $child = $walker.GetFirstChild($Element)
    while ($child) {
        Dump-UiTree -Element $child -Depth ($Depth + 1) -MaxDepth $MaxDepth
        $child = $walker.GetNextSibling($child)
    }
}

function Close-App {
    param($Process, $Window, [int]$GraceMs = 4000)
    if ($Window) {
        try {
            (Get-Pattern $Window ([System.Windows.Automation.WindowPattern]::Pattern)).Close()
        } catch { }
        if ($Process -and $Process.WaitForExit($GraceMs)) { return }
    }
    if ($Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
    }
}

function Close-ProcessByPid {
    param([int]$ProcessId, [int]$GraceMs = 3000)
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (-not $process) { return }
    try { $process.CloseMainWindow() | Out-Null } catch { }
    if ($process.WaitForExit($GraceMs)) { return }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}
