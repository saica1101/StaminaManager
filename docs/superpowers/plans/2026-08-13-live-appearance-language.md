# 外観・ナビゲーション・言語の即時反映 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Acrylic、ナビゲーション、バージョン、言語変更を指定どおり即時反映する。

**Architecture:** 既存の DesktopAcrylicController 基盤と ViewModel を維持する。外観は controller の設定順とプレビュー経路だけを直し、言語は共有リソースコンテキスト更新後に Page ツリーのみ再生成する。

**Tech Stack:** C#、WinUI 3、Windows App SDK、MRT Core、WinUIEx

---

### Task 1: Acrylic とスライダー

**Files:**
- Modify: `StaminaManager/Infrastructure/Windows/AdjustableAcrylicBackdrop.cs`
- Modify: `StaminaManager/MainWindow.xaml.cs`
- Modify: `StaminaManager/ViewModels/SettingsViewModel.cs`
- Modify: `StaminaManager/Views/SettingsPage.xaml.cs`
- Modify: `DESIGN.md`

- [ ] `DesktopAcrylicController.LuminosityOpacity` を adapter と lifecycle へ追加する。
- [ ] 保存済みパーセントを Tint / Luminosity の両方へ適用する。
- [ ] テーマ変更時の順序を reset → theme → opacity にする。
- [ ] Slider の値を同期的にプレビューし、250 ms は保存だけに限定する。
- [ ] 既存 rollback、直列化、Solid fallback を維持する。
- [ ] `fix: Acrylicの即時反映とテーマ切替を修正` でコミットする。

### Task 2: About とバージョン

**Files:**
- Modify: `StaminaManager/MainPage.xaml`
- Modify: `StaminaManager/MainPage.xaml.cs`
- Modify: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- Modify: `StaminaManager/Resources/Strings/en-US/Resources.resw`
- Modify: `DESIGN.md`

- [ ] About を Settings 直後の `MenuItems` へ移す。
- [ ] バージョンを1個の TextBlock と locale 別 format にまとめる。
- [ ] Automation Name / HelpText を同じ1行表示と一致させる。
- [ ] `fix: About配置とバージョン表示を整理` でコミットする。

### Task 3: 言語のライブ切替

**Files:**
- Modify: `StaminaManager/Infrastructure/Resources/AppResourceService.cs`
- Modify: `StaminaManager/ViewModels/SettingsViewModel.cs`
- Modify: `StaminaManager/App.xaml.cs`
- Modify: `StaminaManager/MainPage.xaml.cs`
- Modify: `StaminaManager/Views/CompactPage.xaml.cs`
- Modify: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- Modify: `StaminaManager/Resources/Strings/en-US/Resources.resw`
- Modify: `DESIGN.md`
- Modify: `docs/accessibility-checklist.md`

- [ ] 共有 resource context の language qualifier を切替可能にする。
- [ ] 言語保存・override成功後に App へUI再生成を依頼する。
- [ ] 古いページの外部イベント購読を解除して Page ツリーを置換する。
- [ ] 現在ページ、表示モード、ViewModel、データ、ウィンドウを保持する。
- [ ] 再起動案内を削除し、成功時は新言語で表示する。
- [ ] `feat: 言語設定を再起動なしで反映` でコミットする。

### Task 4: 最小検証

- [ ] `dotnet build StaminaManager/StaminaManager.csproj -c Release -p:Platform=x64`
- [ ] `git diff --check`
- [ ] 新規テスト作成・テストスイート実行・UI自動テストは行わない。
