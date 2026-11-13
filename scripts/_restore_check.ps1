$real = Join-Path $env:APPDATA 'LiveFastTranslation'
Write-Output ('REAL_EXISTS=' + (Test-Path $real))
$backs = Get-ChildItem -Path $env:TEMP -Filter 'lft_dbg_bak_*' -Directory -ErrorAction SilentlyContinue
if ($backs) { foreach ($b in $backs) { Write-Output ('BACKUP=' + $b.FullName) } } else { Write-Output 'NO_DBG_BACKUP' }
$e2eBack = Get-ChildItem -Path $env:TEMP -Filter 'LiveFastTranslation_appdata_backup_*' -Directory -ErrorAction SilentlyContinue
if ($e2eBack) { foreach ($b in $e2eBack) { Write-Output ('E2E_BACKUP=' + $b.FullName) } } else { Write-Output 'NO_E2E_BACKUP' }

# Restore if real missing and a backup exists (prefer newest)
if (-not (Test-Path $real)) {
  $candidate = $null
  if ($e2eBack) { $candidate = $e2eBack | Sort-Object LastWriteTime -Descending | Select-Object -First 1 }
  elseif ($backs) { $candidate = $backs | Sort-Object LastWriteTime -Descending | Select-Object -First 1 }
  if ($candidate) {
    Move-Item -LiteralPath $candidate.FullName -Destination $real -Force
    Write-Output ('RESTORED_FROM=' + $candidate.FullName)
  }
}
Write-Output ('REAL_EXISTS_AFTER=' + (Test-Path $real))
