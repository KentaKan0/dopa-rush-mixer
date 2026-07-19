# DopaRush Mixer 開発ガイド

## 公開リポジトリの方針

- このリポジトリはソースコードのみを公開し、`UNLICENSE` で提供する。バイナリ、生成物、個人設定、秘密情報は含めない。
- 新しいライブラリ、画像、アイコン、フォントを追加する前に、出所とライセンスを確認する。依存関係を追加・更新した場合はライセンス状況も確認する。
- Vivaldi 連携、localhost ブリッジ、拡張機能権限に変更がある場合は、`README.md` と `docs/security-and-privacy.md` を同時に更新する。

## ローカル運用の上書き

- `docs/private/agent-workflow.md` が存在する場合は、このファイルの後に読み込む。このローカル専用ファイルには、Gitリモート、コミット著者、push承認など、環境・所有者固有の運用を記載できる。
- `docs/private/` は公開対象に含めない。

## 実装と検証

- 本番画面は `src/MainWindow.xaml` と `src/App.xaml` を唯一の UI 定義として管理する。
- 文書だけの変更では、対象ファイルの差分確認と `git diff --check` を行う。
- コード、XAML、プロジェクト設定の変更後は、`dotnet build .\DopaRushMixer.csproj --no-restore` を実行する。
- 起動確認が必要な場合、ユーザーが起動中の DopaRushMixer を停止しない。別出力先で検証するか、起動確認を省略した理由を報告する。
- Vivaldi 連携の変更では、拡張機能の手順と localhost ブリッジのプライバシー・セキュリティ上の前提を確認する。

## ドキュメント

- 利用者に見える機能、必要環境、制約、権限、保存データが変わる場合は `README.md` を更新する。
- Vivaldi 連携の扱う情報やローカル通信の前提が変わる場合は `docs/security-and-privacy.md` を更新する。
