param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet('Personal', 'Business')]
    [string]$Usage = 'Personal',
    [string]$LicensePath = '',
    [switch]$AcceptLicense
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '管理者としてPowerShellを実行してください。'
    }
}

function Test-MicrosoftSignedCatalog {
    param([Parameter(Mandatory = $true)][string]$CatalogPath)

    if (-not (Test-Path $CatalogPath -PathType Leaf)) {
        return $false
    }

    try {
        $signature = Get-AuthenticodeSignature -FilePath $CatalogPath
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            Write-Warning "Driver catalog signature is not valid: $($signature.Status)"
            return $false
        }

        if ($null -eq $signature.SignerCertificate) {
            Write-Warning 'Driver catalog has no signer certificate.'
            return $false
        }

        $subject = $signature.SignerCertificate.Subject
        if ($subject -notmatch 'Microsoft') {
            Write-Warning "Driver catalog is not Microsoft signed: $subject"
            return $false
        }

        Write-Host "Microsoft signed driver catalog verified: $subject"
        return $true
    }
    catch {
        Write-Warning "Driver catalog signature verification failed: $($_.Exception.Message)"
        return $false
    }
}

function Invoke-Sc {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    & sc.exe @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed ($LASTEXITCODE): $($Arguments -join ' ')"
    }
}

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [string]$WorkingDirectory = '',
        [string]$Description = ''
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $shortcut.WorkingDirectory = $WorkingDirectory }
    if (-not [string]::IsNullOrWhiteSpace($Description)) { $shortcut.Description = $Description }
    $shortcut.Save()
}

Assert-Administrator

if (-not $AcceptLicense) {
    Write-Host ''
    Write-Host 'ROOOMTECH AI Guard 利用条件'
    Write-Host '  個人の私的利用: 無償'
    Write-Host '  法人・団体・業務利用: 有償・個別見積（Businessライセンス必須）'
    $answer = Read-Host 'LICENSE.mdの利用条件に同意する場合は YES と入力してください'
    if ($answer -ne 'YES') {
        throw '利用条件への同意がないためインストールを中止しました。'
    }
}

if ($Usage -eq 'Business' -and [string]::IsNullOrWhiteSpace($LicensePath)) {
    throw '法人・団体・業務利用では -LicensePath にROOOMTECH発行のBusinessライセンスを指定してください。'
}
if ($Usage -eq 'Business' -and -not (Test-Path $LicensePath -PathType Leaf)) {
    throw "Businessライセンスが見つかりません: $LicensePath"
}

$installRoot = Join-Path $env:ProgramFiles 'ROOOMTECH\AI Guard'
$dataRoot = Join-Path $env:ProgramData 'ROOOMTECH\AIGuard'
$agentSource = Join-Path $PackageRoot 'Agent'
$desktopSource = Join-Path $PackageRoot 'Desktop'
$setupSource = Join-Path $PackageRoot 'Setup'
$scriptsSource = Join-Path $PackageRoot 'scripts'
$configSource = Join-Path $PackageRoot 'config\policy.sample.json'
$driverSource = Join-Path $PackageRoot 'Driver'
$serviceName = 'AIGuardAgent'
$legacyTaskName = 'ROOOMTECH AI Guard Agent'
$uninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ROOOMTECHAIGuard'

if (-not (Test-Path (Join-Path $agentSource 'AIGuard.exe'))) {
    throw "Agentが見つかりません: $agentSource"
}
if (-not (Test-Path (Join-Path $desktopSource 'AIGuard.Desktop.exe'))) {
    throw "Desktopアプリが見つかりません: $desktopSource"
}

Write-Host 'ROOOMTECH AI Guard をインストールしています...'
Write-Host "利用区分: $Usage"

try { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue } catch {}
try { & schtasks.exe /End /TN $legacyTaskName 2>$null | Out-Null } catch {}
try { & schtasks.exe /Delete /TN $legacyTaskName /F 2>$null | Out-Null } catch {}

New-Item -ItemType Directory -Force -Path $installRoot, $dataRoot | Out-Null
$agentDest = Join-Path $installRoot 'Agent'
$desktopDest = Join-Path $installRoot 'Desktop'
$setupDest = Join-Path $installRoot 'Setup'
$scriptsDest = Join-Path $installRoot 'Scripts'
foreach ($path in @($agentDest, $desktopDest, $setupDest, $scriptsDest)) {
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}
Copy-Item -Recurse -Force (Join-Path $agentSource '*') $agentDest
Copy-Item -Recurse -Force (Join-Path $desktopSource '*') $desktopDest
if (Test-Path $setupSource) { Copy-Item -Recurse -Force (Join-Path $setupSource '*') $setupDest }
Copy-Item -Force (Join-Path $scriptsSource 'uninstall.ps1') (Join-Path $scriptsDest 'uninstall.ps1')

$agentExe = Join-Path $agentDest 'AIGuard.exe'
$desktopExe = Join-Path $desktopDest 'AIGuard.Desktop.exe'
$installedLicensePath = Join-Path $dataRoot 'license.json'
$usageModePath = Join-Path $dataRoot 'usage-mode.txt'

if ($Usage -eq 'Business') {
    & $agentExe license-check $LicensePath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'Businessライセンスの署名または有効期限を検証できませんでした。'
    }
    Copy-Item -Force $LicensePath $installedLicensePath
    Set-Content -Path $usageModePath -Value 'Business' -Encoding ASCII
} else {
    Set-Content -Path $usageModePath -Value 'Personal' -Encoding ASCII
}

$policyPath = Join-Path $dataRoot 'policy.json'
if (-not (Test-Path $policyPath) -and (Test-Path $configSource)) {
    Copy-Item $configSource $policyPath
}

$driverPackageInstalled = $false
$inf = Join-Path $driverSource 'AIGuardFilter.inf'
$sys = Join-Path $driverSource 'AIGuardFilter.sys'
$cat = Join-Path $driverSource 'AIGuardFilter.cat'

if ((Test-Path $inf) -and (Test-Path $sys) -and (Test-MicrosoftSignedCatalog $cat)) {
    Write-Host 'Microsoft署名済みKernel Driverをインストールしています...'
    & pnputil.exe /add-driver $inf /install | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Kernel Driver package installation failed. pnputil exit code: $LASTEXITCODE"
    }
    $driverPackageInstalled = $true
}
elseif ((Test-Path $inf) -or (Test-Path $sys) -or (Test-Path $cat)) {
    Write-Warning 'Driver files were found, but a valid Microsoft-signed catalog package was not verified. Kernel Driver installation was skipped.'
}

$serviceBinPath = '"' + $agentExe + '" service'
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $existingService) {
    Invoke-Sc create $serviceName 'binPath=' $serviceBinPath 'start=' 'auto' 'DisplayName=' 'ROOOMTECH AI Guard Agent' 'depend=' 'FltMgr'
} else {
    Invoke-Sc config $serviceName 'binPath=' $serviceBinPath 'start=' 'auto' 'DisplayName=' 'ROOOMTECH AI Guard Agent' 'depend=' 'FltMgr'
}

Invoke-Sc description $serviceName 'ROOOMTECH AI Guard policy agent and kernel protection coordinator'
Invoke-Sc failure $serviceName 'reset=' '86400' 'actions=' 'restart/5000/restart/15000/restart/60000'
Invoke-Sc failureflag $serviceName '1'
Start-Service -Name $serviceName

$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'ROOOMTECH AI Guard.lnk'
New-Shortcut -Path $desktopShortcut -TargetPath $desktopExe -WorkingDirectory (Split-Path $desktopExe) -Description 'ROOOMTECH AI Guard'

$startMenuDir = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'ROOOMTECH AI Guard'
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$appStartShortcut = Join-Path $startMenuDir 'ROOOMTECH AI Guard.lnk'
New-Shortcut -Path $appStartShortcut -TargetPath $desktopExe -WorkingDirectory (Split-Path $desktopExe) -Description 'ROOOMTECH AI Guard'

$installedUninstallScript = Join-Path $scriptsDest 'uninstall.ps1'
$uninstallLauncher = Join-Path $installRoot 'Uninstall-AIGuard.cmd'
$launcherContent = @"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""$installedUninstallScript""'"
"@
Set-Content -Path $uninstallLauncher -Value $launcherContent -Encoding ASCII
$uninstallStartShortcut = Join-Path $startMenuDir 'アンインストール.lnk'
New-Shortcut -Path $uninstallStartShortcut -TargetPath $uninstallLauncher -WorkingDirectory $installRoot -Description 'ROOOMTECH AI Guardをアンインストール'

New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'ROOOMTECH AI Guard'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '1.0.0-rc.2'
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'ROOOMTECH株式会社'
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installRoot
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $desktopExe
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('"' + $uninstallLauncher + '"')
Set-ItemProperty -Path $uninstallKey -Name NoModify -Type DWord -Value 1
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Type DWord -Value 1

Start-Sleep -Milliseconds 750
$service = Get-Service -Name $serviceName -ErrorAction Stop
$filterRunning = $false
try {
    $filterOutput = (& fltmc.exe filters 2>$null | Out-String)
    $filterRunning = $filterOutput -match '(?im)^\s*AIGuardFilter\s+'
} catch {}

Write-Host ''
Write-Host 'インストール完了'
Write-Host "管理画面: $desktopExe"
Write-Host "設定: $policyPath"
Write-Host "利用区分: $Usage"
if ($Usage -eq 'Business') { Write-Host "法人ライセンス: $installedLicensePath" }
Write-Host "Agent Service: $($service.Status) / 自動起動"
if ($filterRunning) {
    Write-Host 'Kernel Driver: 稼働中・ポリシー同期対象'
} elseif ($driverPackageInstalled) {
    Write-Warning 'Kernel Driver packageは導入済みですが、Filter稼働確認ができませんでした。Agent Serviceは自動再試行します。'
} else {
    Write-Warning 'Microsoft署名済みKernel Driverが含まれていないため、Kernelレベルの強制保護はまだ有効ではありません。'
}

$global:LASTEXITCODE = 0
