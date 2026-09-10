# ROOOMTECH AI Guard

Windows 11向けの機密ファイル保護プロジェクトです。

目標は「AIに読めない形式へ変換する」ことではなく、保護対象ファイルへのアクセスをOS側で制御し、許可されていないプロセスからの読み取りを拒否することです。

## MVP構成

- `AI.Guard.Core`: 保護フォルダ判定、許可アプリ判定、SHA-256照合の土台、JSONポリシー
- `AI.Guard.Agent`: コマンドライン判定、Named Pipeによるローカル判定API、JSON Lines監査ログ
- `driver`: Windows File System Minifilter DriverのPoC
- GitHub Actions: Windows上で.NET 8ビルドと拒否判定スモークテスト

## 動作確認

Windows 11で.NET 8 SDKをインストール後、PowerShellから実行します。

~~~powershell
git clone https://github.com/softrenzu/ROOOMTECH-AI_Guard.git
cd ROOOMTECH-AI_Guard
git switch feature/mvp-foundation

$env:AIGUARD_POLICY = "$PWD\config\policy.sample.json"

dotnet run --project .\src\AI.Guard.Agent\AI.Guard.Agent.csproj -- check `
  "C:\AI_Guard_Protected\secret.pdf" `
  "C:\Windows\System32\notepad.exe"
~~~

未許可アプリの場合は `Allowed = false`、終了コード `10` になります。

常駐判定エージェント:

~~~powershell
dotnet run --project .\src\AI.Guard.Agent\AI.Guard.Agent.csproj -- serve
~~~

Named Pipe名は `ROOOMTECH_AIGuard` です。

## 初期ポリシー

既定の保護フォルダは `C:\AI_Guard_Protected` です。初期許可アプリはMicrosoft Word、Excel、PowerPointです。Driver PoCではAdobe Acrobat Readerも許可しています。

## セキュリティ上の位置付け

現在はMVP / PoCです。Minifilter Driverはテスト環境専用です。本番製品にする前に、AgentとDriverの動的ポリシー同期、Authenticode発行者検証、SHA-256検証、ブラウザ子プロセス対策、コピー・印刷・スクリーンショット対策、耐タンパー、正式なドライバー署名、正式Altitude、インストーラー、管理GUIが必要です。

単にプロセス名だけで許可する方式は本番用途では十分ではありません。現在のDriver PoCは、カーネル側でファイルアクセスを拒否できることを検証するための初期実装です。

## 次の実装

管理GUIから「保護フォルダ」と「許可アプリ」を登録し、そのポリシーをAgentからDriverへ安全に同期する構成へ進めます。
