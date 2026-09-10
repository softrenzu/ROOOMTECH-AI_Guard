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
$cat = Join-Path $driverSource 'AIGuardFilter.cat'

if ((Test-Path $inf) -and (Test-Path $sys) -and (Test-MicrosoftSignedCatalog $cat)) {
    Write-Host 'Microsoft署名済みKernel Driverをインストールしています...'
    & pnputil.exe /add-driver $inf /install | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Kernel Driver package installation failed. pnputil exit code: $LASTEXITCODE"
    }

    & fltmc.exe load AIGuardFilter 2>$null
    $driverInstalled = ($LASTEXITCODE -eq 0)
}
elseif ((Test-Path $inf) -or (Test-Path $sys) -or (Test-Path $cat)) {
    Write-Warning 'Driver files were found, but a valid Microsoft-signed catalog package was not verified. Kernel Driver installation was skipped.'
}

Write-Host ''
Write-Host 'インストール完了'
Write-Host "管理画面: $desktopExe"
Write-Host "設定: $policyPath"
if ($driverInstalled) {
    Write-Host 'Kernel Driver: 稼働中'
} else {
    Write-Warning 'Microsoft署名済みKernel Driverが確認できないため、Kernelレベルの強制保護はまだ有効ではありません。'
}
