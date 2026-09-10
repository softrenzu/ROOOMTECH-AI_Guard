# AI Guard Minifilter Driver

ROOOMTECH AI GuardのWindows File System Minifilter Driverです。

## 実装済み

- `IRP_MJ_CREATE`で保護対象ファイルへのオープン要求を監視
- 未許可プロセスからのアクセスをKernel側で `STATUS_ACCESS_DENIED` として拒否
- AgentとのFilter Manager Communication Port (`\ROOOMTECHAIGuardPort`)
- Agentから保護フォルダ最大8件、許可アプリ最大32件を動的同期
- DOSパスをAgent側でNTデバイスパスへ変換してDriverへ送信
- Driver上ではプロセスイメージのフルNTパスを許可リストと照合
- Agent未接続時は開発時の既定保護フォルダ／Office許可ルールへフォールバック

## 製品配布に必要な外部手続き

ソース実装とは別に、一般ユーザーのWindows 11へ安全に配布するため以下が必要です。

1. Microsoftの正式なMinifilter Altitude割り当て
2. Windows Hardware Developer Program / Partner Centerを利用したドライバー署名
3. EVコードサイニング証明書等、Microsoftが要求する署名・提出要件の充足
4. 署名済み `AIGuardFilter.sys` と `AIGuardFilter.cat` の配布パッケージ組み込み

`AIGuardFilter.inf` に現在記載しているAltitude `385201` は開発・テスト用です。正式Altitude取得前に一般配布版で使用しないでください。

## ビルド

Visual Studio 2022以降と対応するWindows Driver Kit (WDK) を使用します。本番配布用はMicrosoft署名済みバイナリのみを使用してください。

## セキュリティ上の境界

AI Guardは通常ユーザーモードのアプリから保護対象ファイルを読み取る経路を制御します。ローカル管理者、Kernel権限を取得した攻撃者、画面を外部カメラで撮影する行為まで完全に防止するものではありません。許可アプリ自体が内容を外部へ転送する場合も別途対策が必要です。
