# ROOOMTECH AI Guard

**生成AI時代の機密ファイル保護ソフト。**

Windows 11上で管理者が指定したフォルダを保護し、許可されていないアプリケーションからファイルを読み取られないよう、OSのファイルI/O層で制御します。

ChatGPT、Claude、Gemini等のサービス名を直接判定するのではなく、ファイルへアクセスするWindowsプロセスを制御します。ブラウザ、Python、AIクライアント等を許可リストに入れなければ、保護対象ファイルへのアクセスを拒否できます。

## 料金・利用区分

- **個人による私的利用: 無償**
- **法人・団体・業務目的での利用: 有償・個別見積**

法人向け価格、導入条件、サポート内容は、端末数・導入規模・運用環境等に応じて個別に設定します。法人利用ではROOOMTECH株式会社が発行する署名付きBusinessライセンスが必要です。

詳細は [LICENSE.md](LICENSE.md) と [docs/COMMERCIAL.md](docs/COMMERCIAL.md) を参照してください。

## 現在のバージョン

**1.0.0-rc.2 Commercial Release Candidate**

管理GUI、Policy Agent、Windows Service常駐、動的Driverポリシー同期、法人ライセンス検証、グラフィカルSetup、Windowsアプリ登録、アンインストール、配布パッケージ生成まで実装済みです。

一般のWindows 11へKernel Driverを正式配布するために必要なMicrosoft正式Minifilter AltitudeおよびMicrosoft Driver署名は外部リリースゲートです。これらが完了するまで、Kernel Driverを含まないRCパッケージではKernelレベルの強制保護は有効になりません。

## 主な機能

- 保護フォルダをGUIで追加・削除
- 保護対象ファイルへの未許可アプリのアクセスをKernel側で拒否
- Word / Excel / PowerPoint等、許可するアプリを管理者が指定
- 許可アプリ登録時にSHA-256を記録
- Agent同期時に許可アプリのSHA-256を再検証
- AgentからMinifilter Driverへポリシーを動的同期
- `AIGuardAgent` Windows Serviceとして自動起動
- Agent異常終了時の自動再起動
- Minifilter停止時の自動ロードと再同期
- Driver / Agent稼働状態表示
- 監査ログ表示
- 個人無償 / 法人有償の利用区分
- ECDSA P-256署名付きBusinessライセンス検証
- 改変・期限切れ法人ライセンスの拒否
- UAC昇格対応の `AIGuard.Setup.exe`
- Windows「インストールされているアプリ」への登録
- デスクトップ / スタートメニューショートカット
- Windows x64自己完結型配布ZIP生成

## 法人ライセンス方式

法人ライセンスはJSON形式で発行し、ROOOMTECH側の秘密鍵でECDSA P-256署名します。製品には公開鍵だけを組み込みます。

ライセンスには、契約先、組織名、ライセンスID、許諾端末数、利用期間を含めます。ファイル内容を変更すると署名検証に失敗します。

検証公開鍵SHA-256:

`43AB1207EB5B04A8027288F970FE1B6F315C7F688FF5D04B75F1357C373D6509`

秘密鍵はリポジトリおよび顧客向け配布物には含めません。

## 仕組み

```text
管理GUI
   |
   +-- 法人ライセンス検証
   |
   v
policy.json
   |
   v
AIGuardAgent Windows Service
   |
   | Filter Manager Communication Port
   v
AI Guard Minifilter Driver
   |
   +-- 許可アプリ    -> アクセス許可
   |
   +-- 未許可アプリ  -> STATUS_ACCESS_DENIED
```

Windows再起動後は `AIGuardAgent` Serviceが自動起動し、Minifilterを必要に応じてロードして現在のポリシーを再同期します。

## 対応環境

- Windows 11 x64
- 管理者権限
- 製品版Kernel保護にはMicrosoft要件を満たした署名済みMinifilter Driverが必要

## インストール

配布ZIPを展開し、`Setup\AIGuard.Setup.exe` をダブルクリックします。

セットアップ画面で次を選択できます。

- 個人による私的利用（無償）
- 法人・団体・業務利用（有償・個別見積）

法人利用ではROOOMTECH株式会社発行のBusinessライセンスJSONを選択します。セットアップが署名・製品名・有効期間を検証し、無効なライセンスでは法人利用としてインストールできません。

管理者向けの自動展開では `scripts\install.ps1` も利用できます。詳しい利用方法は [docs/PRODUCT_GUIDE.md](docs/PRODUCT_GUIDE.md) を参照してください。

## アンインストール

インストール後はWindowsの「設定 > アプリ > インストールされているアプリ」に `ROOOMTECH AI Guard` が登録されます。スタートメニューからもアンインストールできます。

## Windows配布パッケージ

GitHub Actionsの `windows-package` ワークフローが `ROOOMTECH-AI-Guard-Windows-x64.zip` を生成します。

配布物には以下を含みます。

- `Setup/` ダブルクリック用グラフィカルインストーラー
- `Agent/` Policy Agent / Windows Service
- `Desktop/` 管理GUI
- `scripts/install.ps1`
- `scripts/uninstall.ps1`
- `Driver/` Microsoft署名済みDriverがある場合のみ同梱
- `config/` 初期ポリシーとライセンス検証公開鍵
- `docs/COMMERCIAL.md`
- `docs/PRODUCT_GUIDE.md`
- `LICENSE.md`
- `BUILD_INFO.txt`

正式 `vX.Y.Z` タグのパッケージ生成は、有効なMicrosoft署名済みDriverが存在しない場合に失敗するため、Driverなしで正式版を誤公開できない構成です。

## 法人ライセンス発行

ROOOMTECH側では `tools/AI.Guard.LicenseIssuer` を使用してライセンスをオフライン発行できます。

```powershell
dotnet run --project .\tools\AI.Guard.LicenseIssuer\AI.Guard.LicenseIssuer.csproj -- issue `
  --private-key C:\secure\ROOOMTECH_AI_Guard_license_private.pem `
  --licensee "Customer Administrator" `
  --organization "Example Corporation" `
  --seats 25 `
  --valid-until 2027-09-30 `
  --out .\Example-Corporation.aiguard-license.json
```

秘密鍵はGitHub、配布ZIP、顧客端末へ置かないでください。

## セキュリティ境界

本製品は通常ユーザーモードの未許可アプリによる保護ファイルアクセスを制御します。ローカル管理者／Kernel権限を取得した攻撃者、許可アプリ自身による外部転送、外部カメラによる撮影まで完全に防止するものではありません。

詳細は [SECURITY.md](SECURITY.md) を参照してください。

## Microsoft Driverリリース工程

Minifilter Driverの正式配布前に、Microsoftから正式Altitudeを取得し、Microsoftの要件を満たした署名済みDriverを作成します。申請用文面は [docs/ALTITUDE_REQUEST.txt](docs/ALTITUDE_REQUEST.txt) に用意しています。詳細は [docs/PRODUCTION_RELEASE.md](docs/PRODUCTION_RELEASE.md) を参照してください。

---

Copyright © 2026 ROOOMTECH株式会社. All rights reserved.
