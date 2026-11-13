$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repo='C:\Users\at747_loyuw9y\source\repos\Live-Fast_Win-translation'
$exe=Join-Path $repo 'src\LiveFastTranslation\bin\Debug\net8.0-windows\LiveFastTranslation.exe'
$real=Join-Path $env:APPDATA 'LiveFastTranslation'
$backup=Join-Path $env:TEMP ('lft_dbg_bak_'+[guid]::NewGuid().ToString('N'))
$moved=$false
if(Test-Path $real){ Move-Item $real $backup -Force; $moved=$true }
$p=$null
function Wait-Until([scriptblock]$c,[int]$t=10000){$s=[Diagnostics.Stopwatch]::StartNew();while($s.ElapsedMilliseconds -lt $t){try{$r=&$c;if($r){return $r}}catch{};Start-Sleep -Milliseconds 200};return $null}
try{
  $p=Start-Process $exe -PassThru
  $root=[System.Windows.Automation.AutomationElement]::RootElement
  $title='Live-Fast Translation ' + [char]0x2014 + ' ' + [char]0x041D + [char]0x0430 + [char]0x0441 + [char]0x0442 + [char]0x0440 + [char]0x043E + [char]0x0439 + [char]0x043A + [char]0x0438
  $win=Wait-Until { foreach($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)){ try{ if($w.Current.Name -eq $title){ return $w } }catch{} }; $null } 25000
  if(-not $win){ 'NO_WINDOW'; exit 2 }
  Start-Sleep -Milliseconds 1500
  foreach($id in @('NavSections','ArgosPythonCommandCombo','AiProviderCombo','AiModelCombo','AiBaseUrlBox','TestAiButton','CancelButton','SaveButton')){
    $cond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
    $el=$win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$cond)
    Write-Output ($id + '=' + [bool]$el)
  }
} finally {
  if($p -and -not $p.HasExited){ try{$p.CloseMainWindow()|Out-Null}catch{}; Start-Sleep 800; if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
  if(Test-Path $real){ Remove-Item $real -Recurse -Force -ErrorAction SilentlyContinue }
  if($moved -and (Test-Path $backup)){ Move-Item $backup $real -Force }
}
