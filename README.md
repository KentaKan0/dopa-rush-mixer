# DopaRush Mixer

Windows デスクトップに常駐できる、アプリケーションごとの音量ミキサーです。再生中のアプリごとに音量変更とミュート切り替えを行えます。

## プレビュー
<img width="200" height="400" alt="image" src="https://github.com/user-attachments/assets/a36fc115-c2fd-4d0a-8d19-60f667f6a0f9" />

## 主な機能

- アプリケーションごとの音量調整とミュート切り替え
- 出力デバイスの切り替え、マスター音量・ミュートの操作
- 再生中アプリと出力デバイスのピークレベル表示
- 常に手前表示に対応したコンパクトなミキサー画面
- Vivaldiの任意拡張機能による、登録タブごとの音量調整

## 公開方針

このリポジトリは Windows 向けのソースコードを `UNLICENSE` で公開しています。配布バイナリは提供しません。外部からの Pull Request は現在受け付けていません。不具合報告時の注意は [CONTRIBUTING.md](CONTRIBUTING.md) を参照してください。

## 必要環境

- Windows 10 / 11
- .NET 8 SDK 以降（実行には .NET 8 Desktop Runtime 以降）

## 実行

```powershell
dotnet run --project .\DopaRushMixer.csproj
```

ウィンドウは初期状態で常に手前に表示されます。「常に手前」のチェックを外すと通常のウィンドウに戻ります。再生を開始したアプリは最大 2 秒以内にリストへ表示されます。

マスター欄の出力デバイス選択は、閉じた状態では現在選択中のデバイスだけを表示します。展開すると、現在有効な再生デバイスのみから切り替えられます。選択すると Windows の既定出力先（通常・マルチメディア・通信）がそのデバイスへ切り替わります。各音量スライダー下の緑色バーは、現在の音声ピークを約 0.1 秒間隔で表示します。

## セッション単位の制御

一覧の各カードは Windows のオーディオセッション単位です。同じアプリでも複数のセッションを作成していれば、PID とセッション識別子を表示して個別に制御できます。ブラウザのウィンドウ・タブ単位で分かれるかどうかはブラウザ側の実装に依存します。多くのブラウザは複数タブの音声を一つの Windows オーディオセッションへまとめるため、その場合はタブ単位の音量制御はできません。

上部の「出力デバイス（マスター）」では、既定の音声出力デバイス自体の音量とミュートを制御します。

## Vivaldi のタブ単位の制御

`vivaldi-extension/` を Vivaldi の開発者モードで読み込むと、登録したタブをミキサーから個別に制御できます。この連携はローカル利用を前提としたカジュアルな実装であり、認証付きの安全な IPC ではありません。導入、制約、扱う情報、セキュリティ上の前提は [Vivaldi タブ単位の音量制御](docs/vivaldi-tab-audio.md) と [ローカル連携のセキュリティとプライバシー](docs/security-and-privacy.md) を確認してください。

## 配布用ビルド

```powershell
dotnet publish .\DopaRushMixer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

出力先は `bin\Release\net8.0-windows\win-x64\publish\` です。
