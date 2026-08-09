# Phase5A 通知言語同期修正 実装計画

> **実装者向け:** `superpowers:test-driven-development` に従い、各回帰テストを先にRED確認してから最小実装を行う。

**Goal:** 言語変更・復元後に未発火通知を新言語で再予約し、言語同期失敗中のゲーム変更が通知再調整を実行しないようにする。

**Architecture:** 通知キーの構造は変更せず、`NotificationLedgerEntry` のコンテンツfingerprintだけに `AppLanguage` を追加する。旧ledgerのfingerprintは既存の64桁uppercase SHA-256形状検証で読み続け、同一cycleでもfingerprint不一致なら既存の `ReplaceScheduled` を通す。AppCoordinatorでは、言語同期を再試行して成功した場合だけ通知を再調整する共通private経路を初期化・復元・GamesChangedから利用する。

**Tech Stack:** .NET / C# / MSTest / WinUI 3。schema 3、通知ledger JSON schema、通知キー、Store/LocalState/UI automationは変更しない。

---

### Task 1: Major 1の実通知回帰テスト

**Files:**
- Modify: `StaminaManager.Tests/Notifications/NotificationCoordinatorTests.cs`
- Modify: `StaminaManager.Tests/Notifications/NotificationLedgerStoreTests.cs`

- [x] 同一cycleの日本語予約後に設定言語を英語へ変更し、実 `NotificationCoordinator` が `cancel → schedule` を実行し、キーを維持した英語fingerprintを保存するテストを追加する。
- [x] 旧fingerprintを持つledgerが現行storeの検証を通る互換テストを追加する。
- [x] targeted testで現行実装のREDを確認後、修正後にGREENを確認する。

### Task 2: Major 2のGamesChanged回帰テスト

**Files:**
- Modify: `StaminaManager.Tests/Application/AppCoordinatorNotificationTests.cs`

- [x] 初期言語同期失敗後、失敗中のゲーム変更では通知呼び出しが0回であることを追加する。
- [x] 後続ゲーム変更で言語同期が成功した場合、その変更で通知が1回だけ実行されることを同じ実 `AppCoordinator` テストで確認する。
- [x] targeted testで現行の直接通知経路がREDになることを確認後、修正後にGREENを確認する。

### Task 3: 最小実装

**Files:**
- Modify: `StaminaManager.Core/Models/NotificationLedgerEntry.cs`
- Modify: `StaminaManager.Core/Calculations/NotificationStateMachine.cs`
- Modify: `StaminaManager/Application/NotificationCoordinator.cs`
- Modify: `StaminaManager/Application/AppCoordinator.cs`

- [x] fingerprint入力へ言語値を追加し、通知キーの生成式とledger schema・既存fingerprint検証を変更しない。
- [x] `NotificationStateMachine` と disabled経路へ設定言語を渡し、同一cycleの言語差分を既存の `ReplaceScheduled` 判定へ流す。
- [x] `ReconcileLanguageAsync` 後に `IsLanguageSynchronized` がtrueの場合だけ通知を呼ぶ共通経路を作り、初期化・復元・GamesChangedで再利用する。

### Task 4: 検証とコミット

- [x] targeted testsをRED→GREENで再実行した（Release 31件成功）。
- [x] `dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj -c Release -p:Platform=x64` を実行した（690件成功）。
- [x] `./BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -SkipRun /p:Configuration=Release` を実行した（警告0・エラー0）。
- [x] `git diff --check`、禁止対象 `tests/ui/task-backdrop-dialog-polish.ps1` の無変更、schema 3・restore journal ack・backup/tray/UI周辺の差分なしを確認した。
- [x] `fix: 言語変更時の通知再予約を保証` でコミットし、変更・テスト件数・build・commit hashを日本語で報告する。Phase5Bへは進まない。
