# バックドロップ・ゲーム編集・設定表示改善 実装計画

> 対応仕様: `docs/superpowers/specs/2026-08-02-backdrop-dialog-settings-polish-design.md`

## 進め方

- ユーザー所有の未コミット差分を巻き戻さず、その差分を含むファイルでは必要箇所だけを編集する。
- 各タスクは失敗するテストを先に追加し、対象テストの失敗理由を確認してから最小実装を行う。
- 物理ファイルとして残るWinUIExのBlur実装は削除せず、選択・永続化・適用経路から切り離す。

## Task 1: バックドロップ選択の明示マッピング

対象:

- 追加: `StaminaManager.Core/Validation/BackdropPolicy.cs`
- 変更: `StaminaManager/ViewModels/SettingsViewModel.cs`
- 変更: `StaminaManager/Views/SettingsPage.xaml`
- 変更: `StaminaManager/Views/SettingsPage.xaml.cs`
- 変更: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- 変更: `StaminaManager.Tests/Views/SettingsPageAppearanceRoutingContractTests.cs`
- 追加: `StaminaManager.Tests/Validation/BackdropPolicyTests.cs`

手順:

1. `Mica / Acrylic / Solid` のenum↔index双方向変換、旧値のAcrylic正規化、範囲外拒否をテストする。
2. 既存の直接キャストを禁止し、XAMLにBlur/Transparent項目がないことを契約テストへ追加する。
3. `BackdropPolicy` に許可値と変換を集約し、ViewModelとPageから使用する。
4. Blur/TransparentのComboBoxItemと表示リソースを削除する。

検証:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "BackdropPolicyTests|SettingsPageAppearanceRoutingContractTests"
```

## Task 2: 旧バックドロップ設定の互換移行と適用境界

対象:

- 変更: `StaminaManager/Infrastructure/Persistence/DataEnvelopeCodec.cs`
- 変更: `StaminaManager/Infrastructure/Persistence/LocalDataStore.cs`
- 変更: `StaminaManager/Infrastructure/Windows/BackdropService.cs`
- 変更: `StaminaManager/MainWindow.xaml`
- 変更: `StaminaManager/MainWindow.xaml.cs`
- 変更: `StaminaManager.Tests/Persistence/LocalDataStoreTests.cs`
- 変更: `StaminaManager.Tests/Infrastructure/Windows/AppearanceServiceTests.cs`
- 変更: `StaminaManager.Tests/Backup/SafeZipReaderTests.cs`
- 必要に応じて変更: `StaminaManager.Tests/Application/AppCoordinatorTests.cs`

手順:

1. schema 1/2のBlur/TransparentがAcrylicになること、未知値は破損扱いになることをテストする。
2. 旧値を含むprimaryが原子的にAcrylicへ書き戻されることをテストする。
3. 書戻し失敗時も読み込み結果はAcrylicになり、既存ファイルを保持する経路をテスト可能にする。
4. `DataEnvelopeCodec` で旧2値だけを正規化し、`LocalDataStore` でprimaryだけを書き戻す。
5. `BackdropService` へ旧2値が直接渡された場合もAcrylic定義を適用し、未知値は既存の安全フォールバックとする。
6. ユーザーが追加したBlur専用オーバーレイをMainWindowから除き、その他のユーザー変更は保持する。

検証:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "LocalDataStoreTests|AppearanceServiceTests|SafeZipReaderTests|AppCoordinatorTests"
```

## Task 3: 画像1件あたり5MB制限の撤廃

対象:

- 変更: `StaminaManager/Infrastructure/Storage/AssetStore.cs`
- 変更: `StaminaManager/Controls/GameEditorDialog.xaml`
- 変更: `StaminaManager/Controls/GameEditorDialog.xaml.cs`
- 変更: `StaminaManager.Tests/Persistence/AssetStoreTests.cs`

手順:

1. 5MBを超える有効PNG/JPEGを保存できるテストへ置き換える。
2. 入力全体を`byte[]`へ保持しないこと、一時入力ファイルが成功・失敗・キャンセル時に残らないことをテストする。
3. `AssetStore` で入力をアプリ所有の一時ファイルへストリームコピーし、StorageFileのランダムアクセスストリームからデコードする。
4. 既存の4096×4096、PNG/JPEG、再エンコード、ランダム所有名、排他制御を維持する。
5. UIの「5MB以下」と5MB超過エラー文言を削除し、OutOfMemoryExceptionは画像選択境界で専用メッセージへ変換する。

検証:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "AssetStoreTests"
```

## Task 4: バックアップの画像上限撤廃と全体予算

対象:

- 変更: `StaminaManager.Core/Validation/BackupLimits.cs`
- 追加: `StaminaManager/Infrastructure/Backup/ExpandedSizeBudget.cs`
- 変更: `StaminaManager/Infrastructure/Backup/SafeZipReader.cs`
- 変更: `StaminaManager/Infrastructure/Backup/BackupCoordinator.Export.cs`
- 変更: `StaminaManager.Tests/Backup/SafeZipReaderTests.cs`
- 変更: `StaminaManager.Tests/Backup/BackupCoordinatorTests.cs`
- 必要に応じて変更: `StaminaManager.Tests/Backup/RestoreCancellationCleanupTests.cs`

手順:

1. 5MB超の有効画像を含むバックアップを受け付けるテストを追加する。
2. metadata合計、実測展開合計、エクスポート事前計測、途中超過、キャンセルの各失敗で一時ファイルが残らないテストを追加する。
3. `MaxImageBytes` を削除し、manifest/data/全画像で共有する`long`の展開予算を導入する。
4. 読込と書出しの各チャンクで予算を消費し、`MaxExpandedBytes` 超過前に中断する。
5. エクスポート前に画像実サイズ合計を検証し、既存の一時アーカイブ回収を維持する。

検証:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "SafeZipReaderTests|BackupCoordinatorTests|RestoreCancellationCleanupTests"
```

## Task 5: ダイアログテーマ・回復時間ラベル・赤い削除ボタン

対象:

- 変更: `StaminaManager/Resources/DesignTokens.xaml`
- 変更: `StaminaManager/Resources/Styles.xaml`
- 変更: `StaminaManager/Controls/GameEditorDialog.xaml`
- 変更: `StaminaManager/Controls/GameEditorDialog.xaml.cs`
- 変更: `StaminaManager/MainPage.xaml.cs`
- 変更: `StaminaManager/Views/SettingsPage.xaml.cs`
- 変更: `StaminaManager.Tests/Accessibility/AccessibilityPrivacyContractTests.cs`

手順:

1. `RequestedTheme = ActualTheme`、回復時間ラベル、同一ダイアログ状態遷移を契約テストで固定する。
2. Light/Dark/HighContrast用の破壊的操作ブラシと、通常・hover・pressed・disabledを持つスタイルをテストする。
3. 編集状態の削除要求ボタンと、削除確認状態の生成済み主ボタンへ同じ視覚スタイルを適用する。
4. AutomationId/Nameはそれぞれ固有のまま維持し、削除確認の既定ボタンはClose側を維持する。
5. ユーザー追加済みのテーマ継承と回復時間ラベルを整形し、削除確認状態でも同じテーマを使うことを確認する。

検証:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "AccessibilityPrivacyContractTests"
```

## Task 6: Settings日本語化の完成

対象:

- 変更: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- 変更: `StaminaManager.Tests/Accessibility/AccessibilityPrivacyContractTests.cs`

手順:

1. 指定された見出し、ボタン表示、AutomationName、説明の期待値をテストへ追加する。
2. ユーザー変更済みの表示文言を維持し、Export/ImportのAutomationNameから旧表現を除く。
3. Blur/Transparentの不要な表示リソースも削除する。

検証:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "AccessibilityPrivacyContractTests"
```

## Task 7: 統合検証

1. 変更対象テストをまとめて実行する。
2. 全テストを実行する。
3. Release x64をビルドする。
4. `git diff --check` と`git status --short`でユーザー変更と最終差分を確認する。
5. UIテスト環境が利用可能なら、Light/Darkの追加・編集・削除、3背景の反復切替、5MB超画像を確認する。

コマンド:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj
dotnet build StaminaManager.slnx -c Release -p:Platform=x64
git diff --check
git status --short
```
