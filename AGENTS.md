# DopaRush Mixer 開発ガイド

## 公開リポジトリの方針

- このリポジトリはソースコードのみを公開し、`UNLICENSE` で提供する。バイナリ、生成物、個人設定、秘密情報は含めない。
- 外部からの Pull Request は受け付けない。変更はリポジトリ所有者の明示的な依頼に基づいて行う。
- 新しいライブラリ、画像、アイコン、フォントを追加する前に、出所とライセンスを確認する。依存関係を追加・更新した場合はライセンス状況も確認する。
- Vivaldi 連携、localhost ブリッジ、拡張機能権限に変更がある場合は、`README.md` と `docs/security-and-privacy.md` を同時に更新する。

## Git と公開フロー

1. 作業前に `git status --short --branch` と `git fetch origin main` を実行し、`origin/main` を確認する。
2. 未コミット変更または取り込み不能なリモート変更がある場合は、上書きせずに所有者へ確認する。
3. 変更後は公開対象に生成物、`.env`、資格情報、ローカルデータが含まれないことを確認する。
4. コミット前に `git diff --check` を実行し、コミットの Author と Committer が `KentaKan0 <71116092+KentaKan0@users.noreply.github.com>` であることを確認する。
5. `main` への push は所有者が明示的に依頼した場合だけ行う。履歴の書き換えや force-push は、対象・理由・影響を示して明示承認を得た場合だけ `--force-with-lease` を使う。
6. 公開後の著者確認は Contributors 一覧ではなく、GitHub の個別コミットに表示される Author と Committer を正とする。

## 実装と検証

- 本番画面は `src/MainWindow.xaml` と `src/App.xaml` を唯一の UI 定義として管理する。
- 文書だけの変更では、対象ファイルの差分確認と `git diff --check` を行う。
- コード、XAML、プロジェクト設定の変更後は、`dotnet build .\DopaRushMixer.csproj --no-restore` を実行する。
- 起動確認が必要な場合、ユーザーが起動中の DopaRushMixer を停止しない。別出力先で検証するか、起動確認を省略した理由を報告する。
- Vivaldi 連携の変更では、拡張機能の手順と localhost ブリッジのプライバシー・セキュリティ上の前提を確認する。

## ドキュメント

- 利用者に見える機能、必要環境、制約、権限、保存データが変わる場合は `README.md` を更新する。
- Vivaldi 連携の扱う情報やローカル通信の前提が変わる場合は `docs/security-and-privacy.md` を更新する。
- 外部PRを受け付けない方針は `CONTRIBUTING.md` と Pull Request テンプレートを正とする。
