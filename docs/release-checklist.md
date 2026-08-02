# StaminaManager リリースチェックリスト

最終更新: 2026-08-02

このチェックリストは Windows 11 x64 向け初回 Microsoft Store 提出の
現在地を記録する。`[ ]` は未完了であり、Store 提出前に担当者が証跡とともに
確認する。

## x64 Release

- [x] `Directory.Build.props` とアプリプロジェクトの build target が
  `x64` / `win-x64` に固定されている。
- [x] `win-x64.pubxml` が `Release`、`x64`、`win-x64`、self-contained、
  ReadyToRun を指定している。ここでの self-contained は .NET runtime を指し、
  Windows App SDK runtime は manifest の framework dependency を利用する。
- [x] Release unit test が 584 passed / 0 failed で完了している。
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

- [x] Partner Center の予約済み identity へ関連付けている。
  - Package/Identity/Name: `saica1101.StaminaManager`
  - Package/Identity/Publisher:
    `CN=E42D0651-60BF-47A1-BD3B-ECCF464087D2`
  - Package/Properties/PublisherDisplayName: `saica1101`
- [x] Display name、自然な日本語 description、logo、app notification
  activation、無効初期状態の StartupTask を manifest で確認した。
- [x] `internetClient` などの network capability を追加していない。
- [x] 不要な `mp:PhoneIdentity`、phone manifest namespace、旧開発用GUIDを
  manifestから削除した。
- [x] `TargetDeviceFamily`を`Windows.Desktop`だけに限定し、`MinVersion`を
  Windows 11初版の`10.0.22000.0`に設定した。
- [x] manifest versionは4部の数値 `1.0.0.0` であり、生成パッケージと
  一致している。
- [ ] 提出版 version が Partner Center の既存 submission より大きく、
  package と submission で一致している。

## Secret、証明書、署名

- [x] コミット対象を再確認し、パスワード、token、PFX、秘密鍵、
  接続文字列が追跡されていないことを確認した。
- [x] Store提出物は `AppxPackageSigningEnabled=false` で生成し、PFX、
  証明書、パスワードを要求・生成していない。
- [x] `.msixupload` 内のMSIXに `AppxSignature.p7x` が存在しないことを
  確認した。Microsoft Storeがcertification後の配布物へ署名する。
- [ ] Partner Center certification後の配布パッケージの署名を確認した。

## パッケージ認証と Store 提出

- [ ] Windows App Certification Kitは2026年時点でdeprecatedであり、
  ローカルの任意preflightとしては未実行。Partner Center certificationを
  Store受け入れの最終判定とする。
- [x] `BuildStorePackage.ps1 -Version 1.0.0.0` でPartner Center
  関連付け後のx64 `.msixupload` を `artifacts/store/1.0.0.0/<run-id>/`
  に生成した（0 warnings / 0 errors）。
- [x] `.msixupload` のfile listはx64 MSIXと`appxsym`の2件である。
  内包MSIXはName `saica1101.StaminaManager`、Publisher
  `CN=E42D0651-60BF-47A1-BD3B-ECCF464087D2`、Version `1.0.0.0`、
  ProcessorArchitecture `x64` と確認した。生成物は約79.9 MB。
- [x] 内包MSIXのPublisherDisplayName `saica1101`、Windows.Desktop
  MinVersion `10.0.22000.0`、`StaminaManager.exe`の存在、StartupTaskの
  Executable一致、`AppxSignature.p7x`不存在を確認した。さらに
  `AppxBlockMap.xml`と`[Content_Types].xml`の存在、およびBlockMapの
  SHA2-256ハッシュを全ファイル・全64 KiBブロックで検証した。
  disk-backed検証後の一時ディレクトリ残骸は0件。
- [ ] `.msixupload` をPartner Centerへアップロードし、certificationを
  完了した。
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

- [ ] Partner Centerで既存submissionとのversion比較、upload、
  certification、配布物の署名確認を行う。
- [ ] deprecatedなWACKを任意preflightとして実施するかを判断する。
- [ ] taskbar / Start / Store tile の実機目視と Store screenshot は、
  UI 実機試験時に実施する。

## 参照した Microsoft 公式資料

- [Windows アプリのアイコンを作成する](https://learn.microsoft.com/ja-jp/windows/apps/design/iconography/app-icon-construction)
- [MRT Core で言語、スケール、コントラストに合わせてリソースを調整する](https://learn.microsoft.com/windows/apps/windows-app-sdk/mrtcore/tailor-resources-lang-scale-contrast)
- [uap:VisualElements manifest schema](https://learn.microsoft.com/uwp/schemas/appxpackage/uapmanifestschema/element-uap-visualelements)
- [単一プロジェクトMSIXでアプリをパッケージ化する](https://learn.microsoft.com/windows/apps/windows-app-sdk/single-project-msix)
- [製品identityの詳細を表示する](https://learn.microsoft.com/windows/apps/publish/view-app-identity-details)
- [MSIXアプリのパッケージ化](https://learn.microsoft.com/windows/msix/package/packaging-uwp-apps)
- [Microsoft Storeを開始する](https://learn.microsoft.com/windows/apps/publish/get-started)
