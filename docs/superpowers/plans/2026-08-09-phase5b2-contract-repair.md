# Phase5B-2 契約修正 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (recommended) or superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Phase5B-2レビュー指摘を、ネイティブComboBoxの説明と実値ベースのlocale/accessibility契約で最小修正する。

**Architecture:** 既存のSettings event、LanguagePolicy、Appearance Routerは変更しない。XAMLの`x:Uid`からWinUI標準の`ComboBox.Description`と`AutomationProperties.HelpText`をreswへ解決し、契約テストはja/enのXML実値を直接検証する。

**Tech Stack:** WinUI 3 XAML、`.resw`、MSTest、LINQ to XML、.NET 10。

---

### Task 1: RED契約を追加・整理

**Files:**
- Modify: `StaminaManager.Tests/Views/LanguageLocalizationContractTests.cs`
- Modify: `StaminaManager.Tests/Accessibility/AccessibilityPrivacyContractTests.cs`
- Modify: `StaminaManager.Tests/Resources/ResourceLocalizationContractTests.cs`

- [x] `LanguageSelector.Description` と `LanguageSelector.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText` を必須キーへ追加し、ja/enともDescription/HelpTextを`変更はアプリの次回起動時に反映されます。`/`Changes take effect the next time you start the app.`、自称値を`日本語`/`English`とする実値契約を追加する。
- [x] `AccessibilityPrivacyContractTests`でja/enの`Resources.resw`をXML解析し、画像案内に`4096×4096`を含め`5MB`/`5 MB`を含めないこと、回復時間見出しをja=`スタミナが1回復する時間`・en=`Time to recover one stamina`、分/秒ヘッダーを実値で検証する。5MB表記の不在対象へXAML・コード・両localeリソースを含める。
- [x] `LanguageLocalizationContractTests`の不安定な属性全般scannerを、翻訳対象property名の属性値に日本語文字があるかだけを検査する明示範囲へ縮小する。ブランド名・非翻訳英語は検査対象外とし、既存のrequired keyと両locale `x:Uid` baselineは維持する。
- [x] 既存の英語リソース日本語検出は、仕様上の自称`LanguageJapaneseItem.Content`だけを明示的に許可し、それ以外のen-US固定日本語検出は維持する。
- [x] targeted testを実行し、現行XAML/reswが不足するためREDになることを確認する。

### Task 2: 最小のresource修正

**Files:**
- Modify: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- Modify: `StaminaManager/Resources/Strings/en-US/Resources.resw`
- Verify: `StaminaManager/Views/SettingsPage.xaml`の既存`x:Uid="LanguageSelector"`

- [x] ComboBoxへ`x:Uid="LanguageSelector"`で解決されるnative `Description`と`HelpText`のキーを追加し、追加TextBlockは作らない。
- [x] 両localeで選択肢の自称を`日本語`/`English`へ固定する。
- [x] targeted testをGREENにする。

### Task 3: 検証とコミット

**Files:**
- Verify only; do not modify Phase5C or Settings event/LanguagePolicy/Appearance Router files.

- [x] Phase5B-2で停止し、targeted tests、全Release tests、`.\BuildAndRun.ps1 .\StaminaManager\StaminaManager.csproj -SkipRun /p:Configuration=Release`を実行する。
- [x] XAML compile、`git diff --check`、変更ファイルのSHA-256、禁止script無変更、Store/LocalState/実UIなしを確認する。
- [ ] `fix: 言語選択の説明とアクセシビリティ契約を補完`でコミットする。
