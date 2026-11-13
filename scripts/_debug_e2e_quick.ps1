Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
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
function Wait-Until([scriptblock]$cond,[int]$timeoutMs=15000,[int]$stepMs=200){$sw=[Diagnostics.Stopwatch]::StartNew();while($sw.ElapsedMilliseconds -lt $timeoutMs){try{$r=&$cond;if($r){return $r}}catch{};Start-Sleep -Milliseconds $stepMs};return $null}
function Get-SettingsWindow { $desktop=[System.Windows.Automation.AutomationElement]::RootElement; $wins=$desktop.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition); foreach($w in $wins){ try{ if([string]$w.Current.Name -eq $script:SettingsWindowTitle){return $w} }catch{} }; return $null }
function Find-WindowByName([string]$exactName,[int]$timeoutMs=20000){ Wait-Until { $desktop=[System.Windows.Automation.AutomationElement]::RootElement; $children=$desktop.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition); foreach($w in $children){ try{ if($w.Current.Name -eq $exactName){return $w} }catch{} }; $null } $timeoutMs }
function Find-DescById([string]$id, [int]$timeoutMs = 10000) { $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id); Wait-Until { $w = Get-SettingsWindow; if (-not $w) { return $null }; try { $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond) } catch { $null } } $timeoutMs }
try{
  $p=Start-Process $exe -PassThru
  $window=Find-WindowByName $script:SettingsWindowTitle 25000
  Write-Output ('WIN=' + [bool]$window)
  Start-Sleep -Milliseconds 1500
  foreach($id in 'ArgosPythonCommandCombo','CancelButton','NavSections'){
    $el=Find-DescById $id 3000
    Write-Output ($id + '=' + [bool]$el)
  }
} finally {
  if($p -and -not $p.HasExited){ try{$p.CloseMainWindow()|Out-Null}catch{}; Start-Sleep 1000; if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
  if(Test-Path $real){ Remove-Item $real -Recurse -Force -ErrorAction SilentlyContinue }
  if($moved -and (Test-Path $backup)){ Move-Item $backup $real -Force }
}
