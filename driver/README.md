# AI Guard Minifilter Driver

このディレクトリはWindows File System Minifilter Driverの開発用PoCです。

現在のPoCは、パスに `\AI_Guard_Protected\` を含むファイル／フォルダへのユーザーモードからのオープン要求を監視し、次のアプリ以外を拒否します。

- WINWORD.EXE
- EXCEL.EXE
- POWERPNT.EXE
- AcroRd32.exe

## 重要

これは本番用ドライバーではありません。現段階では以下が未実装です。

- ユーザーモードAgentからの動的ポリシー同期
- Authenticode発行者検証
- 実行ファイルSHA-256検証
- プロセス生成元／子プロセス制御
- 耐タンパー
- 正式なMicrosoft署名
- Microsoftから割り当てられた正式Altitude

ドライバーのビルドにはVisual Studio 2022と対応するWindows Driver Kitが必要です。テスト環境以外へインストールしないでください。
