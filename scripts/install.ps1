param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '管理者としてPowerShellを実行してください。'
    }
}

Assert-Administrator

$installRoot = Join-Path $env:ProgramFiles 'ROOOMTECH\AI Guard'
$dataRoot = Join-Path $env:ProgramData 'ROOOMTECH\AIGuard'
$agentSource = Join-Path $PackageRoot 'Agent'
$desktopSource = Join-Path $PackageRoot 'Desktop'
$configSource = Join-Path $PackageRoot 'config\policy.sample.json'
$driverSource = Join-Path $PackageRoot 'Driver'

if (-not (Test-Path (Join-Path $agentSource 'AIGuard.exe'))) {
    throw "Agentが見つかりません: $agentSource"
}
if (-not (Test-Path (Join-Path $desktopSource 'AIGuard.Desktop.exe'))) {
    throw "Desktopアプリが見つかりません: $desktopSource"
}

Write-Host 'ROOOMTECH AI Guard をインストールしています...'

New-Item -ItemType Directory -Force -Path $installRoot, $dataRoot | Out-Null
Copy-Item -Recurse -Force $agentSource (Join-Path $installRoot 'Agent')
Copy-Item -Recurse -Force $desktopSource (Join-Path $installRoot 'Desktop')

$policyPath = Join-Path $dataRoot 'policy.json'
if (-not (Test-Path $policyPath) -and (Test-Path $configSource)) {
    Copy-Item $configSource $policyPath
}

$agentExe = Join-Path $installRoot 'Agent\AIGuard.exe'
$taskName = 'ROOOMTECH AI Guard Agent'
$taskCommand = '"' + $agentExe + '" serve'
& schtasks.exe /Create /TN $taskName /SC ONSTART /RU SYSTEM /RL HIGHEST /TR $taskCommand /F | Out-Null
& schtasks.exe /Run /TN $taskName | Out-Null

$desktopExe = Join-Path $installRoot 'Desktop\AIGuard.Desktop.exe'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'ROOOMTECH AI Guard.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = $desktopExe
$shortcut.WorkingDirectory = Split-Path $desktopExe
$shortcut.Description = 'ROOOMTECH AI Guard'
$shortcut.Save()

$driverInstalled = $false
$inf = Join-Path $driverSource 'AIGuardFilter.inf'
$sys = Join-Path $driverSource 'AIGuardFilter.sys'
if ((Test-Path $inf) -and (Test-Path $sys)) {
    Write-Host '署名済みKernel Driverをインストールしています...'
    & pnputil.exe /add-driver $inf /install | Out-Host
    & fltmc.exe load AIGuardFilter 2>$null
    $driverInstalled = ($LASTEXITCODE -eq 0)
}

Write-Host ''
Write-Host 'インストール完了'
Write-Host "管理画面: $desktopExe"
Write-Host "設定: $policyPath"
if ($driverInstalled) {
    Write-Host 'Kernel Driver: 稼働中'
} else {
    Write-Warning '署名済みKernel Driverがパッケージに含まれていないため、Kernelレベルの強制保護はまだ有効ではありません。'
}
