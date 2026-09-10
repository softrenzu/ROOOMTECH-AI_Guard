$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '管理者としてPowerShellを実行してください。'
}

$taskName = 'ROOOMTECH AI Guard Agent'
$installRoot = Join-Path $env:ProgramFiles 'ROOOMTECH\AI Guard'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'ROOOMTECH AI Guard.lnk'

try { & schtasks.exe /End /TN $taskName | Out-Null } catch {}
try { & schtasks.exe /Delete /TN $taskName /F | Out-Null } catch {}
try { & fltmc.exe unload AIGuardFilter 2>$null | Out-Null } catch {}

if (Test-Path $desktopShortcut) { Remove-Item -Force $desktopShortcut }
if (Test-Path $installRoot) { Remove-Item -Recurse -Force $installRoot }

Write-Host 'ROOOMTECH AI Guard本体を削除しました。'
Write-Host '監査ログとpolicy.jsonは安全のためProgramDataに残しています。不要な場合は手動で削除してください。'
