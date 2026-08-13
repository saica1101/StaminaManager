# 外観・ナビゲーション・言語の即時反映 設計

## 目的

- Acrylic の不透明度を操作中に即時反映する。
- Light / Dark 切替後も Acrylic と XAML UI の配色を一致させる。
- About とバージョン表示を整理する。
- 言語変更をプロセス再起動なしで画面全体へ反映する。

## Acrylic

既存の `AdjustableAcrylicBackdrop` と `DesktopAcrylicController` を継続利用する。
保存済みの整数パーセントを `TintOpacity` と `LuminosityOpacity` の両方へ
同じ 0.0～1.0 値として設定する。スライダーの `ValueChanged` では UI
スレッド上で同期的にプレビューし、250 ms のタイマーは永続化だけに使う。

テーマ変更時は `ResetProperties` で以前のカスタム値を解除してから新しい
`SystemBackdropConfiguration.Theme` を設定し、最後に両 opacity を再適用する。
既存の環境判定、Solid fallback、保存失敗時 rollback は維持する。

## ナビゲーションとバージョン

`About` は `NavigationView.FooterMenuItems` から通常の `MenuItems` へ移し、
`Settings` の直後に置く。`PaneFooter` はバージョン専用とし、表示を1個の
`TextBlock` にまとめる。日本語は `バージョン : 1.0.0`、英語は
`Version: 1.0.0` とする。

## 言語の即時反映

`PrimaryLanguageOverride` の保存・通知 reconciliation を維持する。
適用成功後にアプリ共有の `AppResourceService` の言語 qualifier を更新し、
既存 ViewModel とサービスを保持したまま MainPage 以下の Page ツリーを再生成する。
古い MainPage / CompactPage のイベント購読は明示的に解除する。

これにより `x:Uid` と C# の動的リソースを同じタイミングで更新し、日本語と英語が
混在する部分的なライブ切替を避ける。現在ページ、通常／コンパクト表示、ゲーム、
設定、通知、ウィンドウ状態は保持する。

## 検証範囲

ユーザー指定により、新規テスト作成とテストスイート実行は行わない。変更後は
Release ビルドのみ実行し、未実施の実機確認項目を明記する。
