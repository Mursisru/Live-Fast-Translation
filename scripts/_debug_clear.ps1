$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$script:SettingsWindowTitle = 'Live-Fast Translation ' + [char]0x2014 + ' ' + [char]0x041D + [char]0x0430 + [char]0x0441 + [char]0x0442 + [char]0x0440 + [char]0x043E + [char]0x0439 + [char]0x043A + [char]0x0438
$repo='C:\Users\at747_loyuw9y\source\repos\Live-Fast_Win-translation'
$exe=Join-Path $repo 'src\LiveFastTranslation\bin\Debug\net8.0-windows\LiveFastTranslation.exe'
$real=Join-Path $env:APPDATA 'LiveFastTranslation'
$backup=Join-Path $env:TEMP ('lft_dbg_bak_'+[guid]::NewGuid().ToString('N'))
$moved=$false
if(Test-Path $real){ Move-Item $real $backup -Force; $moved=$true }
$p=$null
function Wait-Until([scriptblock]$c,[int]$t=15000,[int]$s=200){$sw=[Diagnostics.Stopwatch]::StartNew();while($sw.ElapsedMilliseconds -lt $t){try{$r=&$c;if($r){return $r}}catch{};Start-Sleep -Milliseconds $s};return $null}
function Get-SettingsWindow { $root=[System.Windows.Automation.AutomationElement]::RootElement; $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition); foreach($w in $wins){ try{ if([string]$w.Current.Name -eq $script:SettingsWindowTitle){return $w} }catch{} }; return $null }
function Find-DescById([string]$id,[int]$timeoutMs=8000){ $cond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id); $sw=[Diagnostics.Stopwatch]::StartNew(); while($sw.ElapsedMilliseconds -lt $timeoutMs){ try{ $w=Get-SettingsWindow; if($w){ $el=$w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$cond); if($el){return $el} } }catch{}; Start-Sleep -Milliseconds 200 }; return $null }
function Find-WindowByName([string]$exactName,[int]$timeoutMs=20000){ Wait-Until { $root=[System.Windows.Automation.AutomationElement]::RootElement; $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition); foreach($w in $wins){ try{ if($w.Current.Name -eq $exactName){return $w} }catch{} }; $null } $timeoutMs }
try{
  $p=Start-Process $exe -PassThru
  $win=Find-WindowByName $script:SettingsWindowTitle 25000
  Start-Sleep -Milliseconds 1500
  # go to Advanced (index 5)
  $w=Get-SettingsWindow
  $navCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'NavSections')
  $nav=$w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$navCond)
  $itemCond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)
  $items=$nav.FindAll([System.Windows.Automation.TreeScope]::Descendants,$itemCond)
  $sel=$items[5].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern); $sel.Select(); Start-Sleep -Milliseconds 600
  $btn=Find-DescById 'ClearHistoryButton' 5000
  if(-not $btn){ 'NO_BTN'; exit 3 }
  # invoke in background job-like? no easy; but try async invoke via BeginInvoke not available. Just invoke and see
  $inv=$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  # Start invoke in separate runspace? complicated. Instead, enumerate windows right before and rely on timing - but invoke blocks. 
  Write-Output 'INVOKING'
  $inv.Invoke()
  Write-Output 'INVOKED'
} finally {
  if($p -and -not $p.HasExited){ try{$p.CloseMainWindow()|Out-Null}catch{}; Start-Sleep 800; if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
  if(Test-Path $real){ Remove-Item $real -Recurse -Force -ErrorAction SilentlyContinue }
  if($moved -and (Test-Path $backup)){ Move-Item $backup $real -Force }
}
