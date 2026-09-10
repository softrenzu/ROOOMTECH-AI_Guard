# ROOOMTECH AI Guard 製品ガイド

## 1. 製品概要

ROOOMTECH AI Guardは、Windows 11上の指定フォルダを保護し、許可されていないアプリケーションからのファイルアクセスを制御するソフトウェアです。

## 2. 利用区分

- 個人が自己の私的目的で利用する場合: 無償
- 法人、団体、組織、または業務目的で利用する場合: 有償・個別見積

法人利用ではROOOMTECH株式会社が発行した署名付きBusinessライセンスが必要です。

## 3. インストール

配布ZIPを展開し、`Setup\AIGuard.Setup.exe` をダブルクリックします。UACの確認後、画面上で利用区分を選択します。

### 個人私的利用

「個人による私的利用（無償）」を選択し、利用条件を確認・同意して「インストール」を押します。

### 法人・団体・業務利用

「法人・団体・業務利用（有償・個別見積）」を選択し、ROOOMTECH株式会社から受領したBusinessライセンスJSONを指定します。

セットアップはライセンスの署名、製品名、利用期間を検証します。検証できない場合は法人利用としてインストールできません。

管理者向けの自動展開では、PowerShellによるサイレント相当のインストールも利用できます。

```powershell
.\scripts\install.ps1 -Usage Personal -AcceptLicense
```

```powershell
.\scripts\install.ps1 -Usage Business -LicensePath "C:\path\company.aiguard-license.json" -AcceptLicense
```

## 4. 初期設定

インストール後、デスクトップまたはスタートメニューの `ROOOMTECH AI Guard` を起動します。

1. 「保護設定」で保護フォルダを追加します。
2. 保護ファイルを開くことを許可するアプリを追加します。
3. 「設定を保存」を押します。
4. Kernel DriverとPolicy Agentの状態が「稼働中」であることを確認します。

許可アプリ追加時には実行ファイルのSHA-256を記録します。

## 5. 法人ライセンス

「ライセンス」タブで現在の利用状態を確認できます。

法人ライセンスを後から登録する場合は「法人ライセンスを読み込む」を選択し、ROOOMTECH株式会社から受領したJSONライセンスファイルを指定します。

## 6. 監査ログ

「監査ログ」タブでは直近のアクセス判定を確認できます。ログ実体は `%ProgramData%\ROOOMTECH\AIGuard\audit.jsonl` に保存されます。

## 7. アンインストール

Windowsの「設定 > アプリ > インストールされているアプリ」から `ROOOMTECH AI Guard` を選んでアンインストールできます。

スタートメニューの `ROOOMTECH AI Guard > Uninstall` も利用できます。

管理者向けには配布ZIPの `scripts\uninstall.ps1` から削除することもできます。

## 8. 注意事項

Microsoft署名済みKernel Driverがパッケージに含まれないRC版では、GUIとPolicy Agentは動作しますがKernelレベルの強制保護は有効になりません。

製品のセキュリティ境界と既知の制約は `SECURITY.md` を参照してください。
