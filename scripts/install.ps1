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

Assert-Administrator

$installRoot = Join-Path $env:ProgramFiles 'ROOOMTECH\AI Guard'
$dataRoot = Join-Path $env:ProgramData 'ROOOMTECH\AIGuard'
$agentSource = Join-Path $PackageRoot 'Agent'
$desktopSource = Join-Path $PackageRoot 'Desktop'
$configSource = Join-Path $PackageRoot 'config\policy.sample.json'
$driverSource = Join-Path $PackageRoot 'Driver'
$serviceName = 'AIGuardAgent'
$legacyTaskName = 'ROOOMTECH AI Guard Agent'

if (-not (Test-Path (Join-Path $agentSource 'AIGuard.exe'))) {
    throw "Agentが見つかりません: $agentSource"
}
if (-not (Test-Path (Join-Path $desktopSource 'AIGuard.Desktop.exe'))) {
    throw "Desktopアプリが見つかりません: $desktopSource"
}

Write-Host 'ROOOMTECH AI Guard をインストールしています...'

try { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue } catch {}
try { & schtasks.exe /End /TN $legacyTaskName 2>$null | Out-Null } catch {}
try { & schtasks.exe /Delete /TN $legacyTaskName /F 2>$null | Out-Null } catch {}

New-Item -ItemType Directory -Force -Path $installRoot, $dataRoot | Out-Null
$agentDest = Join-Path $installRoot 'Agent'
$desktopDest = Join-Path $installRoot 'Desktop'
if (Test-Path $agentDest) { Remove-Item -Recurse -Force $agentDest }
if (Test-Path $desktopDest) { Remove-Item -Recurse -Force $desktopDest }
New-Item -ItemType Directory -Force -Path $agentDest, $desktopDest | Out-Null
Copy-Item -Recurse -Force (Join-Path $agentSource '*') $agentDest
Copy-Item -Recurse -Force (Join-Path $desktopSource '*') $desktopDest

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

$agentExe = Join-Path $installRoot 'Agent\AIGuard.exe'
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

$desktopExe = Join-Path $installRoot 'Desktop\AIGuard.Desktop.exe'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'ROOOMTECH AI Guard.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = $desktopExe
$shortcut.WorkingDirectory = Split-Path $desktopExe
$shortcut.Description = 'ROOOMTECH AI Guard'
$shortcut.Save()

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
Write-Host "Agent Service: $($service.Status) / 自動起動"
if ($filterRunning) {
    Write-Host 'Kernel Driver: 稼働中・ポリシー同期対象'
} elseif ($driverPackageInstalled) {
    Write-Warning 'Kernel Driver packageは導入済みですが、Filter稼働確認ができませんでした。Agent Serviceは自動再試行します。'
} else {
    Write-Warning 'Microsoft署名済みKernel Driverが含まれていないため、Kernelレベルの強制保護はまだ有効ではありません。'
}

# Optional legacy-task and filter probes must not leak a stale native exit code
# after a successful installation.
$global:LASTEXITCODE = 0
