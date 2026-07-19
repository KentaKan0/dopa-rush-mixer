# DopaRush Mixer 開発ガイド

## UI 実装

- 本番画面は `src/MainWindow.xaml` と `src/App.xaml` を唯一の UI 定義として管理する。
- UI の変更後は通常の `dotnet build` で XAML とバインディングの整合性を確認する。
