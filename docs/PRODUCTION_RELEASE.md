# ROOOMTECH AI Guard 製品版リリース条件

対象: Windows 11 x64 / ROOOMTECH AI Guard 1.0

## 実装済み

- Windows File System Minifilterによる保護ファイル読み取り拒否
- 保護フォルダの動的設定
- 許可アプリの動的設定
- Agent -> Driverポリシー同期
- 許可アプリ登録時のSHA-256記録と同期時再検証
- Windows管理GUI
- Agent自動起動
- 監査ログ表示
- 自己完結型Windows x64ビルド
- 配布ZIP生成
- インストール／アンインストールスクリプト
- 個人利用無償、法人・団体・業務利用は有償・個別見積の利用条件

## 一般配布前の外部リリースゲート

Kernel Driverを通常のWindows 11へ配布するには、ソースコード完成とは別にMicrosoft側の手続きが必要です。

1. Microsoftから正式なMinifilter Altitudeを取得する。
2. 取得したAltitudeを `driver/AIGuardFilter.inf` の開発用値と置き換える。
3. Microsoft Partner Center / Windows Hardware Developer Programの要件を満たす。
4. Microsoftの要求に従ってドライバーパッケージを署名・提出する。
5. 署名済み `AIGuardFilter.sys` と必要な署名成果物を配布パッケージへ組み込む。
6. 実機Windows 11でインストール、再起動、Office/PDF閲覧、ブラウザ/Python等からの拒否、アンインストールを検証する。

Microsoft Learn:
- https://learn.microsoft.com/windows-hardware/drivers/ifs/minifilter-altitude-request
- https://learn.microsoft.com/windows-hardware/drivers/ifs/load-order-groups-and-altitudes-for-minifilter-drivers
- https://learn.microsoft.com/windows-hardware/drivers/ifs/creating-an-inf-file-for-a-minifilter-driver
- https://learn.microsoft.com/windows-hardware/drivers/dashboard/code-signing-reqs

## 製品版判定

上記の外部リリースゲートを通過し、署名済みDriverを含むパッケージの実機試験が完了した時点で `1.0.0` とします。

それまでは `1.0.0-rc.x` とし、一般ユーザーへ「Kernel保護が有効な正式版」と表示しません。
