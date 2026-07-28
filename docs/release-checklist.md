# StaminaManager リリースチェックリスト

最終更新: 2026-07-29

このチェックリストは Windows 11 x64 向け初回 Microsoft Store 提出の
現在地を記録する。`[ ]` は未完了であり、Store 提出前に担当者が証跡とともに
確認する。

## x64 Release

- [x] `Directory.Build.props` とアプリプロジェクトの build target が
  `x64` / `win-x64` に固定されている。
- [x] `win-x64.pubxml` が `Release`、`x64`、`win-x64`、self-contained、
  ReadyToRun を指定している。ここでの self-contained は .NET runtime を指し、
  Windows App SDK runtime は manifest の framework dependency を利用する。
- [x] Release unit test が 408 passed / 0 failed で完了している。
- [x] `BuildAndRun.ps1 -SkipRun` による x64 Release build が、
  0 warnings / 0 errors で成功している。
- [x] パッケージ起動した Release build の UI 自動試験が
  16 passed / 0 failed / 17 conditional skipped で完了し、試験前後で
  Data と Settings のパス・サイズ・SHA-256 が一致している。
- [x] Release 出力に意図しない x86 / ARM64 バイナリが含まれていない。

## ブランド資産

- [x] `Assets/Brand/AppIconSource.svg` が唯一の編集用正本であり、
  ゲーム会社・ゲームパブリッシャーの mark や文字を含まない。
- [x] Square 44、Square 150、Wide、Splash、Store の
  `scale-100/125/150/200/400` が生成されている。
- [x] Square 44 の app-list target size に default、dark theme 用
  `altform-unplated`、light theme 用 `altform-lightunplated` がある。
- [x] App icon は 16 / 24 / 32 / 48 / 256 px を最低限含み、
  小サイズでも arc と中央 tick を識別できる。
- [x] 代表 asset の透過と light / dark 背景での配色差分を確認した。
- [ ] インストール済み Release build で、light / dark の taskbar、Start、
  Store tile、Splash を実機目視した。

## Manifest、identity、version

- [x] ローカル開発用 identity
  `35F978A4-DE78-42D1-AA68-26AAB5754821` と
  `CN=AppPublisher` を維持している。
- [x] Display name、自然な日本語 description、logo、app notification
  activation、無効初期状態の StartupTask を manifest で確認した。
- [x] `internetClient` などの network capability を追加していない。
- [x] ローカル manifest version は `1.0.0.0` である。
- [ ] Partner Center の予約済み Store identity / Publisher へ関連付けた。
- [ ] 提出版 version が Partner Center の既存 submission より大きく、
  package と submission で一致している。

## Secret、証明書、署名

- [x] コミット対象を再確認し、パスワード、token、PFX、秘密鍵、
  接続文字列が追跡されていないことを確認した。
- [ ] 署名用 PFX をリポジトリ外または ignore 済み `artifacts/` に置いた。
- [ ] 証明書パスワードをコード・引数ログ・ドキュメントへ記録せず、
  `STAMINA_CERT_PASSWORD` など安全な実行時入力から渡した。
- [ ] Release MSIX を提出先に合う証明書で署名し、署名を検証した。

## パッケージ認証と Store 提出

- [ ] 署名済み x64 MSIX に Windows App Certification Kit を実行し、
  required test が 0 failed である。
- [ ] WACK report を `artifacts/wack-report.xml` に保存し、失敗・警告を
  レビューした。
- [ ] Partner Center 関連付け後の x64 `.msixupload` を生成した。
- [ ] `.msixupload` の package identity、architecture、version、
  file list を提出前に確認した。
- [ ] Store listing 用 screenshot を実アプリから作成し、ゲーム会社の
  mark、個人データ、秘密情報が写っていないことを確認した。
- [ ] Store listing の説明、system requirements、既知の制約、
  support contact を確定した。

## Privacy と data handling

- [x] manifest に network capability がなく、ゲーム情報と設定は
  ローカル保存を前提としている。
- [ ] Store の privacy / data handling 回答を実装と照合し、収集なし・
  外部送信なしという現在の設計から逸脱していないことを確認した。
- [ ] privacy policy URL が提出カテゴリ上必要かを Partner Center で確認し、
  必要な場合は公開 URL を登録した。
- [ ] screenshot、診断資料、WACK report にユーザー登録データが含まれない。

## 現在の外部依存

- [ ] `STAMINA_CERT_PASSWORD` が未設定のため、開発証明書と署名 MSIX は
  未生成。値をリポジトリへ保存しない。
- [ ] Partner Center の予約済み Store identity / Publisher が未関連付けの
  ため、Store 用 `.msixupload` は未生成。
- [ ] WACK は署名済み MSIX と管理者権限での実行が必要なため未実行。
- [ ] taskbar / Start / Store tile の実機目視と Store screenshot は、
  UI 実機試験時に実施する。

## 参照した Microsoft 公式資料

- [Windows アプリのアイコンを作成する](https://learn.microsoft.com/ja-jp/windows/apps/design/iconography/app-icon-construction)
- [MRT Core で言語、スケール、コントラストに合わせてリソースを調整する](https://learn.microsoft.com/windows/apps/windows-app-sdk/mrtcore/tailor-resources-lang-scale-contrast)
- [uap:VisualElements manifest schema](https://learn.microsoft.com/uwp/schemas/appxpackage/uapmanifestschema/element-uap-visualelements)
