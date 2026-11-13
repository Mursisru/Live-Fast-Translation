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
try{
  $p=Start-Process $exe -PassThru
  $root=[System.Windows.Automation.AutomationElement]::RootElement
  $sw=[Diagnostics.Stopwatch]::StartNew()
  $win=$null
  $title='Live-Fast Translation ' + [char]0x2014 + ' ' + [char]0x041D + [char]0x0430 + [char]0x0441 + [char]0x0442 + [char]0x0440 + [char]0x043E + [char]0x0439 + [char]0x043A + [char]0x0438
  while($sw.ElapsedMilliseconds -lt 25000 -and -not $win){
    foreach($w in $root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)){
      try{ if($w.Current.Name -eq $title){ $win=$w; break } }catch{}
    }
    if(-not $win){ Start-Sleep -Milliseconds 300 }
  }
  if(-not $win){ Write-Output 'NO_WINDOW'; exit 2 }
  Write-Output ('WINDOW=' + $win.Current.Name)
  $all=$win.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $ids=@()
  foreach($e in $all){
    try{ $id=$e.Current.AutomationId; if($id){ $ids += $id } }catch{}
  }
  $ids | Sort-Object -Unique
  Write-Output ('---COUNT:' + $ids.Count)
} finally {
  if($p -and -not $p.HasExited){ try{$p.CloseMainWindow()|Out-Null}catch{}; Start-Sleep 800; if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
  if(Test-Path $real){ Remove-Item $real -Recurse -Force -ErrorAction SilentlyContinue }
  if($moved -and (Test-Path $backup)){ Move-Item $backup $real -Force }
}
