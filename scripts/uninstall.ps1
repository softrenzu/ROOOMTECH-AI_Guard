$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '管理者としてPowerShellを実行してください。'
}

$serviceName = 'AIGuardAgent'
$legacyTaskName = 'ROOOMTECH AI Guard Agent'
$installRoot = Join-Path $env:ProgramFiles 'ROOOMTECH\AI Guard'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'ROOOMTECH AI Guard.lnk'
$startMenuDir = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'ROOOMTECH AI Guard'
$uninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ROOOMTECHAIGuard'

try { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue } catch {}
try { & sc.exe delete $serviceName 2>$null | Out-Null } catch {}
try { & schtasks.exe /End /TN $legacyTaskName 2>$null | Out-Null } catch {}
try { & schtasks.exe /Delete /TN $legacyTaskName /F 2>$null | Out-Null } catch {}
try { & fltmc.exe unload AIGuardFilter 2>$null | Out-Null } catch {}
try { & sc.exe delete AIGuardFilter 2>$null | Out-Null } catch {}

if (Test-Path $desktopShortcut) { Remove-Item -Force $desktopShortcut }
if (Test-Path $startMenuDir) { Remove-Item -Recurse -Force $startMenuDir }
if (Test-Path $uninstallKey) { Remove-Item -Recurse -Force $uninstallKey }
if (Test-Path $installRoot) { Remove-Item -Recurse -Force $installRoot }

Write-Host 'ROOOMTECH AI Guard本体・Agent Service・Filter Service・Windowsアプリ登録を削除しました。'
Write-Host '監査ログ、policy.json、usage-mode.txt、法人ライセンスは安全のためProgramDataに残しています。不要な場合は手動で削除してください。'

$global:LASTEXITCODE = 0
