# ROOOMTECH AI Guard

**生成AI時代の機密ファイル保護ソフト。**  
Windows 11上で、管理者が指定したフォルダのファイルを、許可されていないアプリから読めないように制御します。

ChatGPT、Claude、Gemini等のサービスそのものを判定するのではなく、ファイルを読み取ろうとするWindowsプロセスをOSのファイルI/O層で制御する設計です。ブラウザ、Python、AIクライアント等を許可リストに入れなければ、保護対象ファイルの読み取りを拒否できます。

## 料金

- **個人による私的利用: 無償**
- **法人・団体・業務目的での利用: 有償・個別見積**

法人向けの価格、導入条件、サポート内容は利用規模・環境に応じて個別に設定します。詳細は [LICENSE.md](LICENSE.md) を参照してください。

## 対応環境

- Windows 11 x64
- 管理者権限
- 製品版Kernel保護にはMicrosoft要件を満たした署名済みMinifilter Driverが必要

## 現在のバージョン

**1.0.0 Release Candidate**

管理GUI、Policy Agent、Windows Service常駐、動的Driverポリシー同期、配布パッケージ生成まで実装済みです。一般のWindows 11へKernel Driverを配布するために必要なMicrosoft正式AltitudeおよびDriver署名は外部リリースゲートです。詳細は [docs/PRODUCTION_RELEASE.md](docs/PRODUCTION_RELEASE.md) を参照してください。

## 主な機能

- 保護フォルダをGUIで追加・削除
- 保護対象ファイルへの未許可アプリの読み取りをKernel側で拒否
- Word / Excel / PowerPoint等、許可するアプリを管理者が指定
- 許可アプリ登録時にSHA-256を記録
- Agent同期時に許可アプリのSHA-256を再検証
- AgentからMinifilter Driverへポリシーを動的同期
- `AIGuardAgent` Windows Serviceとして自動起動
- Agent異常終了時の自動再起動
- 起動時にMinifilterが停止していれば自動ロードしてポリシーを再同期
- Driver / Agent稼働状態表示
- 監査ログ表示
- Windows x64自己完結型配布ZIP生成

## 仕組み

~~~text
管理GUI
   |
   v
policy.json
   |
   v
AIGuardAgent Windows Service
   |
   v
AI Guard Agent
   |
   | Filter Manager Communication Port
   v
AI Guard Minifilter Driver
   |
   +-- 許可アプリ    -> 読み取り許可
   |
   +-- 未許可アプリ  -> STATUS_ACCESS_DENIED
~~~

保護フォルダの閲覧自体は可能にしつつ、ファイル内容を読み取る要求を制御します。Windows再起動後は `AIGuardAgent` Serviceが自動起動し、Minifilterを必要に応じてロードして現在のポリシーを再同期します。

## Windows配布パッケージ

GitHub Actionsの `windows-package` ワークフローが以下を含む `ROOOMTECH-AI-Guard-Windows-x64.zip` を生成します。

- `Agent/` Policy Agent / Windows Service実行ファイル
- `Desktop/` 管理GUI
- `scripts/install.ps1`
- `scripts/uninstall.ps1`
- `Driver/` Microsoft署名済みDriverがある場合のみ同梱
- `config/` 初期ポリシー

`scripts/install.ps1` は管理者権限で実行し、AgentをLocalSystemの自動起動Windows Service `AIGuardAgent` として登録します。旧RC版のScheduled Taskが存在する場合は削除し、Service障害時の再起動設定も行います。

署名済みDriverがまだ含まれないRCパッケージでは、GUIとAgentは利用できますがKernelレベルの強制保護は有効になりません。正式版ではMicrosoft署名済みDriverを含めます。

## 開発環境での確認

Windows 11 + .NET 8 SDK:

~~~powershell
git clone https://github.com/softrenzu/ROOOMTECH-AI_Guard.git
cd ROOOMTECH-AI_Guard

$env:AIGUARD_POLICY = "$PWD\config\policy.sample.json"

dotnet run --project .\src\AI.Guard.Agent\AI.Guard.Agent.csproj -- check `
  "C:\AI_Guard_Protected\secret.pdf" `
  "C:\Windows\System32\notepad.exe"
~~~

未許可アプリの場合は `Allowed = false`、終了コード `10` になります。

Agentをコンソールで起動する場合:

~~~powershell
dotnet run --project .\src\AI.Guard.Agent\AI.Guard.Agent.csproj -- serve
~~~

Driver同期:

~~~powershell
dotnet run --project .\src\AI.Guard.Agent\AI.Guard.Agent.csproj -- sync-driver
~~~

通常のインストールでは `scripts/install.ps1` が `AIGuardAgent` Windows Serviceを登録するため、`serve` を手動起動する必要はありません。

## セキュリティ境界

本製品は通常ユーザーモードの未許可アプリによる保護ファイル読み取りを制御します。ローカル管理者／Kernel権限を取得した攻撃者、許可アプリ自身による外部転送、外部カメラによる撮影まで完全に防止するものではありません。

詳細は [SECURITY.md](SECURITY.md) を参照してください。

## Microsoft Driverリリース工程

Minifilter Driverの正式配布前に、Microsoftから正式Altitudeを取得し、Microsoftの要件を満たした署名済みDriverを作成します。申請用文面は [docs/ALTITUDE_REQUEST.txt](docs/ALTITUDE_REQUEST.txt) に用意しています。

---

Copyright © 2026 ROOOMTECH株式会社. All rights reserved.
