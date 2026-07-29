# Recovery Seconds and Per-Game Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 分＋秒単位のスタミナ回復、ゲーム別通知、1日未満の秒表示、スキーマ1から2への安全な移行を実装する。

**Architecture:** `StaminaManager.Core` のゲームモデルへ回復秒と個別通知を追加し、計算は合計秒へ正規化する。保存層にはスキーマ別DTOを使う共通コーデックを置き、ローカルデータとバックアップで同じ移行・厳格検証を行う。通知OFFを状態機械の最優先分岐とし、UIは既存のMVVMとFluent標準コントロールを維持する。

**Tech Stack:** C# 14、.NET 10、WinUI 3、Windows App SDK 2.3.1、CommunityToolkit.Mvvm 8.4.2、System.Text.Json source generation、MSTest、winapp UI Automation

---

## 実行場所と共通ルール

- Worktree: `D:\Programming\CSharp\StaminaManager\.worktrees\initial-implementation`
- Branch: `codex/feature/initial-implementation`
- Spec: `docs/superpowers/specs/2026-07-29-recovery-seconds-per-game-notifications-appearance-stability-design.md`
- 本計画は外観安定化計画より先に実行する。
- 各Task開始前に `git status --short` と `git log --oneline -5` を確認する。
- `@superpowers:test-driven-development` に従い、必ずREDを確認してから実装する。
- UI作業では `@winui-design`、起動確認では `@winui-dev-workflow`、UI試験では
  `@winui-ui-testing` を適用する。
- ファイル編集には `apply_patch` を使用する。
- ユーザー所有の未コミット変更を戻さない。特に
  `StaminaManager/MainPage.xaml.cs` のダイアログ `RequestedTheme` 初期化を維持する。
- コミットはTask単位、Conventional Commits、日本語subjectとする。

## ファイル構成

### 新規作成

```text
StaminaManager/Infrastructure/Persistence/
├─ DataEnvelopeCodec.cs       # スキーマ判定、移行、現行モデル検証
└─ LegacyDataEnvelope.cs      # スキーマ1専用DTO
```

### 主な変更

```text
StaminaManager.Core/
├─ Models/GameEntry.cs
├─ Persistence/DataEnvelope.cs
├─ Calculations/StaminaCalculator.cs
├─ Calculations/RemainingTimeParts.cs
├─ Calculations/NotificationStateMachine.cs
├─ Validation/GameDraft.cs
├─ Validation/GameEntryValidator.cs
└─ Validation/GameEditPolicy.cs
StaminaManager/
├─ Application/GameManager.cs
├─ Application/NotificationCoordinator.cs
├─ Application/TimerCoordinator.cs
├─ Controls/GameEditorDialog.xaml
├─ Controls/GameCardControl.xaml.cs
├─ ViewModels/GameEditorViewModel.cs
├─ ViewModels/CompactViewModel.cs
├─ Infrastructure/Persistence/JsonSerializationContext.cs
├─ Infrastructure/Persistence/LocalDataStore.cs
├─ Infrastructure/Backup/BackupArchiveValidator.cs
├─ Infrastructure/Backup/SafeZipReader.cs
└─ Resources/Strings/ja-JP/Resources.resw
StaminaManager.Tests/
├─ Calculations/StaminaCalculatorTests.cs
├─ Calculations/RemainingTimePartsTests.cs
├─ Validation/GameEntryValidatorTests.cs
├─ Validation/GameEditPolicyTests.cs
├─ Persistence/LocalDataStoreTests.cs
├─ Backup/SafeZipReaderTests.cs
├─ Application/GameManagerTests.cs
├─ Notifications/NotificationStateMachineTests.cs
├─ Notifications/NotificationCoordinatorTests.cs
├─ ViewModels/GameEditorViewModelTests.cs
├─ ViewModels/CompactViewModelTests.cs
└─ Application/TimerCoordinatorTests.cs
tests/ui/StaminaManager.UiTests.ps1
```

### Task 1: 回復間隔を合計秒へ拡張する

**Files:**
- Modify: `StaminaManager.Core/Models/GameEntry.cs`
- Modify: `StaminaManager.Core/Validation/GameDraft.cs`
- Modify: `StaminaManager.Core/Calculations/StaminaCalculator.cs`
- Modify: `StaminaManager.Core/Validation/GameEntryValidator.cs`
- Test: `StaminaManager.Tests/Calculations/StaminaCalculatorTests.cs`
- Test: `StaminaManager.Tests/Validation/GameEntryValidatorTests.cs`
- Mechanical constructor updates: `StaminaManager`, `StaminaManager.Core`, `StaminaManager.Tests` 内の `GameEntry` / `GameDraft` 生成箇所

- [ ] **Step 1: 秒単位回復と境界値の失敗テストを書く**

`StaminaCalculatorTests` へ次の明示的な境界表を追加する。

| 回復間隔 | 経過 | 期待回復数 | 目的 |
|---|---:|---:|---|
| 0分1秒 | 0秒 / 1秒 / 2秒 | 0 / 1 / 2 | 最小値と直前・一致・直後 |
| 0分59秒 | 58秒 / 59秒 / 60秒 | 0 / 1 / 1 | 秒上限直前 |
| 1分0秒 | 59秒 / 60秒 / 61秒 | 0 / 1 / 1 | 分への繰り上がり |
| 8分30秒 | 509秒 / 510秒 / 511秒 | 0 / 1 / 1 | 分+秒の代表値 |
| 525600分0秒 | 最大値の1秒前 / 一致 | 0 / 1 | 許可最大値 |

`nowUtc` が `RecordedAtUtc` より前でも回復数0となることも固定日時で検証する。
最大間隔で最大スタミナまで回復する `FullAtUtc` が正確であることと、日時加算が
`DateTimeOffset.MaxValue` を超える入力では既存の計算不能結果を維持することも検証する。

```csharp
[TestMethod]
public void Calculate_RecoversAtWholeMinuteAndSecondInterval()
{
    GameEntry entry = CreateEntry(
        current: 0,
        maximum: 2,
        recoveryMinutes: 8,
        recoverySeconds: 30);

    Assert.AreEqual(
        0,
        StaminaCalculator.Calculate(
            entry,
            RecordedAtUtc.AddMinutes(8).AddSeconds(29)).Current);
    StaminaSnapshot recovered = StaminaCalculator.Calculate(
        entry,
        RecordedAtUtc.AddMinutes(8).AddSeconds(30));
    Assert.AreEqual(1, recovered.Current);
    Assert.AreEqual(
        RecordedAtUtc.AddMinutes(17),
        recovered.FullAtUtc);
}
```

`GameEntryValidatorTests` へ `0分0秒`、`0分1秒`、`0分59秒`、`1分0秒`、
`525600分0秒`、`525600分1秒`、秒の-1/60を検証するデータテストを追加する。
合計0秒と最大超過は分・秒のどちらか片方ではなく、回復間隔グループのエラーキー
`RecoveryInterval` へ格納されることを検証する。

- [ ] **Step 2: REDを確認する**

Run:

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~StaminaCalculatorTests|FullyQualifiedName~GameEntryValidatorTests"
```

Expected: `RecoverySeconds` が存在しない、または8分30秒を8分として扱うためFAIL。

- [ ] **Step 3: 現行モデルへ必須項目を追加する**

`GameEntry` と `GameDraft` の末尾へ、既定値なしで次を追加する。

```csharp
int RecoverySeconds,
bool IsNotificationEnabled
```

既存生成箇所はすべて `RecoverySeconds: 0, IsNotificationEnabled: true` を明示してコンパイルを回復する。対象は次で列挙する。

```powershell
rg -n "new GameEntry\(|new GameDraft\(|\b(GameEntry|GameDraft)\s+[A-Za-z_]\w*\s*=\s*new\s*\(|RecoveryMinutes:" StaminaManager.Core StaminaManager StaminaManager.Tests
```

target-typed `new(...)` も必ず含め、少なくとも
`StaminaManager/ViewModels/GameEditorViewModel.cs`、`GameEntryValidator.cs`、およびテスト内の
全生成箇所が列挙されることを確認する。対象テストをGREENにする前に同じ検索を再実行し、
新引数未指定の生成箇所が0件であることを確認する。

- [ ] **Step 4: バリデーションと計算を合計秒へ変更する**

`GameEntryValidator` では次の値を一度だけ算出して使用する。

```csharp
long recoveryIntervalSeconds = checked(
    (long)draft.RecoveryMinutes * 60 + draft.RecoverySeconds);
```

許可範囲は1～`MaxRecoveryMinutes * 60`秒とし、秒部分は0～59とする。
`StaminaCalculator` は経過秒と `TimeSpan.TicksPerSecond` を使用する。

```csharp
long elapsedSeconds = Math.Max(
    0L,
    (long)Math.Floor((nowUtc - recordedAtUtc).TotalSeconds));
long recovered = elapsedSeconds / recoveryIntervalSeconds;
long recoveryTicks = checked(
    checked(remainingStamina * recoveryIntervalSeconds)
    * TimeSpan.TicksPerSecond);
```

- [ ] **Step 5: 対象テストをGREENにする**

Run: Step 2と同じ。

Expected: 対象テストPASS、ビルド警告0。

- [ ] **Step 6: コミットする**

```powershell
$constructorFiles = @(rg -l "new GameEntry\(|new GameDraft\(|\b(GameEntry|GameDraft)\s+[A-Za-z_]\w*\s*=\s*new\s*\(|RecoveryMinutes:" StaminaManager.Core StaminaManager StaminaManager.Tests)
git add -- StaminaManager.Core/Models/GameEntry.cs StaminaManager.Core/Validation/GameDraft.cs StaminaManager.Core/Calculations/StaminaCalculator.cs StaminaManager.Core/Validation/GameEntryValidator.cs StaminaManager.Tests/Calculations/StaminaCalculatorTests.cs StaminaManager.Tests/Validation/GameEntryValidatorTests.cs
git add -p -- $constructorFiles
git diff --cached --check
git diff --cached --name-only
git commit -m "feat: スタミナ回復を秒単位へ拡張"
```

対話stageでは新しい必須引数へ `RecoverySeconds: 0, IsNotificationEnabled: true` を加える
機械的hunkだけを選ぶ。コミット前にユーザー変更4ファイルと、このTaskに無関係なhunkが
cached差分へ含まれないことを確認する。

### Task 2: スキーマ1を厳格にスキーマ2へ移行する

**Files:**
- Modify: `StaminaManager.Core/Persistence/DataEnvelope.cs`
- Create: `StaminaManager/Infrastructure/Persistence/LegacyDataEnvelope.cs`
- Create: `StaminaManager/Infrastructure/Persistence/DataEnvelopeCodec.cs`
- Modify: `StaminaManager/Infrastructure/Persistence/JsonSerializationContext.cs`
- Modify: `StaminaManager/Infrastructure/Persistence/LocalDataStore.cs`
- Modify: `StaminaManager/Infrastructure/Backup/BackupArchiveValidator.cs`
- Modify: `StaminaManager/Infrastructure/Backup/SafeZipReader.cs`
- Test: `StaminaManager.Tests/Persistence/LocalDataStoreTests.cs`
- Test: `StaminaManager.Tests/Backup/SafeZipReaderTests.cs`
- Test: `StaminaManager.Tests/Backup/BackupCoordinatorTests.cs`

- [ ] **Step 1: 移行・必須項目・回復コピーの失敗テストを書く**

追加するテスト:

- スキーマ1 JSONを読むとスキーマ2、0秒、個別通知ONになる。
- スキーマ2 JSONから `recoverySeconds` または `isNotificationEnabled` を欠落させると破損になる。
- 破損プライマリと正常スキーマ1回復コピーの組合せで `Recovery` を返す。
- バックアップの `manifest.dataSchemaVersion` と内部 `schemaVersion` が不一致なら拒否する。
- 固定のmanifest v1 + data v1アーカイブを正常に読み、0秒・個別通知ONへ正規化する。
- 新規exportのmanifestとdataがどちらもスキーマ2になる。

スキーマ1 fixtureは文字列置換で現行JSONから作らず、旧形式を明示した固定JSONにする。

```json
{
  "schemaVersion": 1,
  "games": [{
    "id": "11111111-1111-1111-1111-111111111111",
    "name": "Legacy",
    "baseStamina": 10,
    "maxStamina": 100,
    "recoveryMinutes": 8,
    "recordedAtUtc": "2026-07-29T00:00:00+00:00",
    "imageAssetId": null,
    "sortOrder": 0
  }],
  "settings": {
    "theme": "Light",
    "backdrop": "Mica",
    "notificationsEnabled": true,
    "notificationLeadMinutes": 15,
    "closeBehavior": "MinimizeToTray",
    "startupEnabled": false,
    "lastDisplayMode": "Standard",
    "selectedCompactGameId": null
  }
}
```

- [ ] **Step 2: REDを確認する**

Run:

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~LocalDataStoreTests|FullyQualifiedName~SafeZipReaderTests|FullyQualifiedName~BackupCoordinatorTests"
```

Expected: スキーマ1が拒否されるか、スキーマ2欠落項目が既定値で受理されてFAIL。

- [ ] **Step 3: スキーマ別DTOと共通コーデックを実装する**

`DataEnvelope.CurrentSchemaVersion` を2にする。`LegacyDataEnvelope` と
`LegacyGameEntry` はスキーマ1の全必須コンストラクター引数だけを持つ。

`DataEnvelopeCodec` は次の責務だけを持つ。

```csharp
internal sealed record DecodedDataEnvelope(
    int SourceSchemaVersion,
    DataEnvelope Envelope);

internal static class DataEnvelopeCodec
{
    internal static DecodedDataEnvelope Deserialize(JsonElement root);
    internal static DataEnvelope NormalizeAndValidate(DataEnvelope envelope);
}
```

`Deserialize` はルートの `schemaVersion` を厳格に読み、1ならLegacy DTOから
`RecoverySeconds: 0, IsNotificationEnabled: true` を補って変換し、2なら現行
`DataEnvelope` を読む。現行モデルの新引数は必須なので、スキーマ2の欠落は
`JsonException` になる。未知スキーマは `InvalidDataException` にする。

`JsonSerializationContext` には次の生成メタデータを追加し、コーデックは必ずその
`JsonTypeInfo` を使う。reflection fallbackへ依存しない。

```csharp
[JsonSerializable(typeof(LegacyDataEnvelope))]
[JsonSerializable(typeof(DataEnvelope))]
internal partial class JsonSerializationContext : JsonSerializerContext;
```

- [ ] **Step 4: ローカル保存とバックアップを共通コーデックへ接続する**

`LocalDataStore.TryReadValidEnvelopeAsync` はサイズ確認後に `JsonDocument.ParseAsync` し、
コーデックで移行・検証する。保存はスキーマ2以外を拒否する。

`BackupArchiveValidator.DeserializeData` は `DecodedDataEnvelope` を返し、
`SafeZipReader` は次を検証してから正規化済みEnvelopeを使用する。

```csharp
if (manifest.DataSchemaVersion != decoded.SourceSchemaVersion)
{
    throw new InvalidDataException(
        "The manifest and data schema versions do not match.");
}
```

`ValidateManifest` はデータスキーマ1/2を受け付け、マニフェスト自身のスキーマは1を維持する。

- [ ] **Step 5: 対象テストをGREENにする**

Run: Step 2と同じ。

Expected: ローカル移行、旧バックアップ読込、新規スキーマ2 export、欠落拒否、
不一致拒否、回復コピーがすべてPASS。

- [ ] **Step 6: コミットする**

```powershell
git add -- StaminaManager.Core/Persistence StaminaManager/Infrastructure/Persistence StaminaManager/Infrastructure/Backup StaminaManager.Tests/Persistence/LocalDataStoreTests.cs StaminaManager.Tests/Backup/SafeZipReaderTests.cs StaminaManager.Tests/Backup/BackupCoordinatorTests.cs
git commit -m "feat: データスキーマ2への安全な移行を追加"
```

### Task 3: 編集フローへ回復秒と個別通知を伝播する

**Files:**
- Modify: `StaminaManager.Core/Validation/GameEditPolicy.cs`
- Modify: `StaminaManager/Application/GameManager.cs`
- Modify: `StaminaManager/ViewModels/GameEditorViewModel.cs`
- Test: `StaminaManager.Tests/Validation/GameEditPolicyTests.cs`
- Test: `StaminaManager.Tests/Application/GameManagerTests.cs`
- Test: `StaminaManager.Tests/ViewModels/GameEditorViewModelTests.cs`

- [ ] **Step 1: 編集ポリシーとViewModelの失敗テストを書く**

追加するテスト:

- 回復秒変更で `RecordedAtUtc` が保存時刻へ更新される。
- 個別通知だけの変更では `BaseStamina` と `RecordedAtUtc` が維持される。
- 新規エディターは8分0秒・個別通知ON。
- 編集エディターは保存済み秒・通知状態を復元する。
- 分・秒それぞれが非整数、`NaN`、正の無限大、負の無限大なら保存不可。
- 秒が-1/60、合計0秒、許可最大超過なら保存不可。

- [ ] **Step 2: REDを確認する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~GameEditPolicyTests|FullyQualifiedName~GameManagerTests|FullyQualifiedName~GameEditorViewModelTests"
```

Expected: 新プロパティが保存されない、または時刻更新条件が不足してFAIL。

- [ ] **Step 3: 最小実装を行う**

`GameEditPolicy` のスタミナ変更判定へ秒を追加し、戻り値へ通知状態を常にコピーする。

```csharp
bool hasStaminaChanges =
    initialDraft.CurrentStamina != editedDraft.CurrentStamina
    || initialDraft.MaxStamina != editedDraft.MaxStamina
    || initialDraft.RecoveryMinutes != editedDraft.RecoveryMinutes
    || initialDraft.RecoverySeconds != editedDraft.RecoverySeconds;
```

`GameEditorViewModel` へ次を追加する。

```csharp
[ObservableProperty]
public partial double RecoverySeconds { get; set; }

[ObservableProperty]
public partial bool IsNotificationEnabled { get; set; }

[ObservableProperty]
public partial string? RecoverySecondsError { get; private set; }

[ObservableProperty]
public partial string? RecoveryIntervalError { get; private set; }
```

分と秒それぞれの非整数、`NaN`、正負の無限大、範囲外は対応する入力エラーへ、
`0分0秒` と許可最大超過は `GameEntryValidator.RecoveryIntervalField` の結果を
`RecoveryIntervalError` へ割り当てる。保存可否は3つのエラーすべてを参照する。

`CreateDraftOrThrow`、`Validate`、`GameManager.AddAsync/EditAsync` の全経路へ両値を渡す。

- [ ] **Step 4: 対象テストをGREENにする**

Run: Step 2と同じ。

Expected: 対象テストPASS。

- [ ] **Step 5: コミットする**

```powershell
git add -- StaminaManager.Core/Validation/GameEditPolicy.cs StaminaManager/Application/GameManager.cs StaminaManager/ViewModels/GameEditorViewModel.cs StaminaManager.Tests/Validation/GameEditPolicyTests.cs StaminaManager.Tests/Application/GameManagerTests.cs StaminaManager.Tests/ViewModels/GameEditorViewModelTests.cs
git commit -m "feat: ゲーム編集へ回復秒と個別通知を追加"
```

### Task 4: 個別通知OFFを最優先で処理する

**Files:**
- Modify: `StaminaManager.Core/Calculations/NotificationStateMachine.cs`
- Modify: `StaminaManager/Application/NotificationCoordinator.cs`
- Test: `StaminaManager.Tests/Notifications/NotificationStateMachineTests.cs`
- Test: `StaminaManager.Tests/Notifications/NotificationCoordinatorTests.cs`

- [ ] **Step 1: 予約解除と抑止サイクルの失敗テストを書く**

追加するテスト:

- 計算不能、既存予約あり、通知OFFでもActionが`Cancel`になる。
- 計算不能、既存台帳`Scheduled`、Windows予約なし、通知OFFでは台帳を`Suppressed`にする。
- 台帳なし・予約なし・通知OFFでも計算可能なら`Suppressed`を返す。
- OFF中に通知時刻を過ぎ、ONへ戻すと`ShowImmediate`ではなく`Consumed`になる。
- Settings OFF＋個別OFF、Settings OFF＋個別ONはいずれも全予約を取消・抑止する。
- Settings ON＋個別OFFは対象ゲームだけ取消・抑止する。
- Settings ON＋個別ONは通常どおり予約する。
- `GamesChanged` で個別OFFへ変えると取消され、ONへ戻すと現在サイクルだけ再評価される。

```csharp
NotificationDecision disabled = NotificationStateMachine.Evaluate(
    game,
    leadMinutes,
    nowUtc,
    notificationsEnabled: false,
    existingEntry: null,
    isScheduledInWindows: false);
Assert.AreEqual(NotificationState.Suppressed, disabled.Entry!.State);
```

- [ ] **Step 2: REDを確認する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~NotificationStateMachineTests|FullyQualifiedName~NotificationCoordinatorTests"
```

Expected: 現行実装は計算不能で`InvalidSchedule`を先に返すか、全ゲームを有効として扱いFAIL。

- [ ] **Step 3: 通知無効専用分岐を状態機械の先頭へ追加する**

`StaminaCalculator.Calculate` より前に通知OFFを分岐する。

```csharp
if (!notificationsEnabled)
{
    return EvaluateDisabled(
        game,
        leadMinutes,
        nowUtc,
        existingEntry,
        isScheduledInWindows);
}
```

`EvaluateDisabled` はActionのCancel判定を先に確定する。その後、計算できれば現在サイクルを
`Suppressed` で新規作成し、計算不能なら既存`Scheduled`を`Suppressed`へ変更する。
OFF経路では`InvalidSchedule`を返さない。

- [ ] **Step 4: コーディネーターへ実効状態を渡す**

Settings OFFの`SuppressAllAsync`は維持する。Settings ONのゲームループでは次を渡す。

```csharp
notificationsEnabled: game.IsNotificationEnabled
```

個別OFFでも `ApplyPlatformActionAsync` と `UpdateLedger` を通し、CancelとSuppressed保存を
同一チェックポイントで完了する。

- [ ] **Step 5: 対象テストをGREENにする**

Run: Step 2と同じ。

Expected: 4組合せと抑止サイクル回帰がPASS。

- [ ] **Step 6: コミットする**

```powershell
git add -- StaminaManager.Core/Calculations/NotificationStateMachine.cs StaminaManager/Application/NotificationCoordinator.cs StaminaManager.Tests/Notifications/NotificationStateMachineTests.cs StaminaManager.Tests/Notifications/NotificationCoordinatorTests.cs
git commit -m "feat: ゲーム別の通知設定を予約へ反映"
```

### Task 5: 1日未満を毎秒表示する

**Files:**
- Modify: `StaminaManager.Core/Calculations/RemainingTimeParts.cs`
- Modify: `StaminaManager/Controls/GameCardControl.xaml.cs`
- Modify: `StaminaManager/ViewModels/CompactViewModel.cs`
- Modify: `StaminaManager/Application/TimerCoordinator.cs`
- Modify: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- Test: `StaminaManager.Tests/Calculations/RemainingTimePartsTests.cs`
- Test: `StaminaManager.Tests/ViewModels/CompactViewModelTests.cs`
- Test: `StaminaManager.Tests/Application/TimerCoordinatorTests.cs`

- [ ] **Step 1: 秒表示と1日境界の失敗テストを書く**

追加する期待値:

| Remaining | Days | Hours | Minutes | Seconds | 表示 |
|---|---:|---:|---:|---:|---|
| 1.1秒 | 0 | 0 | 0 | 2 | `00:00:02` |
| 1:02:03 | 0 | 1 | 2 | 3 | `01:02:03` |
| 23:59:59.1 | 0 | 24 | 0 | 0 | `24:00:00` |
| 1日 | 1 | 0 | 0 | 0 | `1日 00:00` |

`TimerCoordinatorTests` ではフェイクTickSourceが受け取るintervalを1秒と検証する。

- [ ] **Step 2: REDを確認する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~RemainingTimePartsTests|FullyQualifiedName~CompactViewModelTests|FullyQualifiedName~TimerCoordinatorTests"
```

Expected: `Seconds`がなく、更新間隔が30秒のためFAIL。

- [ ] **Step 3: 秒分解と表示リソースを実装する**

`RemainingTimeParts` に `Seconds` を追加する。1日未満は
`Ceiling(remaining.TotalSeconds)` を使用し、Hoursを24で剰余化しない。1日以上は従来の
分切り上げを維持する。

`Resources.resw` へ追加する。

```xml
<data name="RemainingTimeHoursSecondsFormat" xml:space="preserve">
  <value>満タンまで {0:00}:{1:00}:{2:00}</value>
</data>
```

カードとCompactは同じリソースIDを使用し、Compactの日本語ハードコードを除く。

- [ ] **Step 4: 表示中だけ1秒更新にする**

```csharp
public static readonly TimeSpan RefreshInterval =
    TimeSpan.FromSeconds(1);
```

通知再調整や保存をTickへ追加しない。既存の可視状態開始・停止処理を維持する。

- [ ] **Step 5: 対象テストをGREENにする**

Run: Step 2と同じ。

Expected: 秒表示、24時間境界、1秒TickがPASS。

- [ ] **Step 6: コミットする**

```powershell
git add -- StaminaManager.Core/Calculations/RemainingTimeParts.cs StaminaManager/Controls/GameCardControl.xaml.cs StaminaManager/ViewModels/CompactViewModel.cs StaminaManager/Application/TimerCoordinator.cs StaminaManager/Resources/Strings/ja-JP/Resources.resw StaminaManager.Tests/Calculations/RemainingTimePartsTests.cs StaminaManager.Tests/ViewModels/CompactViewModelTests.cs StaminaManager.Tests/Application/TimerCoordinatorTests.cs
git commit -m "feat: 満タンまでの残り時間を秒表示"
```

### Task 6: ゲーム編集ダイアログへ分・秒と個別通知を追加する

**Files:**
- Modify: `StaminaManager/Controls/GameEditorDialog.xaml`
- Modify: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- Modify: `tests/ui/StaminaManager.UiTests.ps1`
- Modify: `tests/ui/task11-notification-integration.ps1`

- [ ] **Step 1: UI Automationの失敗テストを追加する**

必須AutomationIdへ次を追加する。

```powershell
'RecoverySecondsInput',
'RecoverySecondsErrorText',
'RecoveryIntervalErrorText',
'GameNotificationToggle'
```

ゲーム追加テストで次を順に検証する。

1. `1分1.5秒`では `RecoverySecondsErrorText` が表示され、Saveしてもダイアログが閉じず
   `data.json` が変更されない。
2. `0分0秒`では `RecoveryIntervalErrorText` が表示され、Saveしてもダイアログが閉じず
   `data.json` が変更されない。
3. `8分30秒`・個別通知OFFへ直すと両エラーが消えて保存でき、`data.json` の
   `recoveryMinutes`、`recoverySeconds`、`isNotificationEnabled` が一致する。

エラー要素の可視性と名前、対応入力のAutomation HelpText、保存不能の3点を照合する。

`task11-notification-integration.ps1` のfixtureをスキーマ2へ更新し、個別ON/OFFの2ゲームを
用意する。次の実予約・台帳シナリオを追加する。

1. Settings ONでは個別ONだけWindows予約があり、個別OFFは`Suppressed`。
2. Settings OFFでは両方のWindows予約がなく、両方とも`Suppressed`。
3. SettingsをONへ戻しても個別OFFは予約されず、個別ONだけ予約される。
4. ゲーム編集で個別ONをOFFへ変えると`GamesChanged`経由で予約が取消される。
5. 同じゲームをONへ戻すと、通知時刻前なら予約、時刻後なら即時toastなしで`Consumed`。

各段階でWindows scheduled toast一覧と `notification-state.json` の両方を照合する。
テスト前後のアプリデータ復元と予約cleanupは既存の `finally` を維持する。

- [ ] **Step 2: 現行ビルドでREDを確認する**

```powershell
.\BuildAndRun.ps1 .\StaminaManager\StaminaManager.csproj -Detach
```

出力PIDを使って次を実行する。

```powershell
.\tests\ui\StaminaManager.UiTests.ps1 -AppPid <PID>
.\tests\ui\task11-notification-integration.ps1 -AppPid <PID>
```

Expected: 通常UIテストは新AutomationIdが存在せずFAIL。拡張した通知統合テストも
`GameNotificationToggle` が存在しないため、ゲーム編集段階でFAIL。各scriptのcleanupが
完了したことを確認してからGREEN実装へ進む。

- [ ] **Step 3: Fluent標準コントロールでUIを実装する**

既存`RecoveryMinutesInput`を2列Gridの左へ移し、Minimumを0にする。右へ次を追加する。

```xml
<NumberBox
    x:Name="RecoverySecondsInput"
    x:Uid="RecoverySecondsInput"
    AutomationProperties.AutomationId="RecoverySecondsInput"
    Header="秒"
    Minimum="0"
    Maximum="59"
    SpinButtonPlacementMode="Compact"
    ValidationMode="Disabled"
    Value="{x:Bind ViewModel.RecoverySeconds, Mode=TwoWay}" />
<TextBlock
    x:Name="RecoverySecondsErrorText"
    AutomationProperties.AutomationId="RecoverySecondsErrorText"
    AutomationProperties.LiveSetting="Assertive"
    AutomationProperties.Name="{x:Bind ViewModel.RecoverySecondsError, Mode=OneWay}"
    Text="{x:Bind ViewModel.RecoverySecondsError, Mode=OneWay}"
    Visibility="{x:Bind local:GameEditorDialog.TextToVisibility(ViewModel.RecoverySecondsError), Mode=OneWay}" />
<TextBlock
    x:Name="RecoveryIntervalErrorText"
    AutomationProperties.AutomationId="RecoveryIntervalErrorText"
    AutomationProperties.LiveSetting="Assertive"
    AutomationProperties.Name="{x:Bind ViewModel.RecoveryIntervalError, Mode=OneWay}"
    Text="{x:Bind ViewModel.RecoveryIntervalError, Mode=OneWay}"
    Visibility="{x:Bind local:GameEditorDialog.TextToVisibility(ViewModel.RecoveryIntervalError), Mode=OneWay}" />
```

画像選択の前へ次を追加する。

```xml
<ToggleSwitch
    x:Name="GameNotificationToggle"
    x:Uid="GameNotificationToggle"
    AutomationProperties.AutomationId="GameNotificationToggle"
    Header="満タン通知"
    IsOn="{x:Bind ViewModel.IsNotificationEnabled, Mode=TwoWay}"
    OffContent="オフ"
    OnContent="オン" />
```

補足文は「Settingsの通知がONの場合に、このゲームの満タン通知を受け取ります」とする。
エラーは分・秒グループ直下に `RecoveryIntervalError` として表示し、入力単体の
`RecoveryMinutesError` / `RecoverySecondsError` も対応する入力へ関連付ける。
エラー文とAutomationPropertiesによって伝え、色だけに依存しない。

`Resources.resw` には両UIDのHeader/OnContent/OffContentに加え、次の明示的な名前を追加する。

```xml
<data name="RecoverySecondsInput.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
  <value>スタミナが1回復する時間（秒）</value>
</data>
<data name="GameNotificationToggle.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name" xml:space="preserve">
  <value>このゲームの満タン通知を切り替える</value>
</data>
```

- [ ] **Step 4: ビルドとUIテストをGREENにする**

Run: Step 2のビルドと対象UIテストに加え、既存アクセシビリティ契約を実行する。

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~AccessibilityPrivacyContractTests"
```

Expected: 秒フィールドエラーと合計間隔エラーが対応位置へ表示され、不正時は保存不能。
正常な分・秒・通知状態は保存され、ダイアログを閉じても値が維持される。

- [ ] **Step 5: 実予約・台帳の通知統合テストをGREENにする**

```powershell
.\tests\ui\task11-notification-integration.ps1 -AppPid <PID>
```

Expected: 実予約、取消、再有効化、`GamesChanged`、台帳遷移がすべてPASS。

- [ ] **Step 6: スクリーンショットを確認する**

```powershell
winapp ui screenshot -a <PID> -o "$env:TEMP\StaminaManager-game-editor-seconds.png"
```

確認項目: ラベル切れなし、スピンボタン重なりなし、Tab順、Light/Dark、補足文の可読性。

- [ ] **Step 7: コミットする**

```powershell
git add -- StaminaManager/Controls/GameEditorDialog.xaml StaminaManager/Resources/Strings/ja-JP/Resources.resw tests/ui/StaminaManager.UiTests.ps1 tests/ui/task11-notification-integration.ps1
git commit -m "feat: ゲーム編集へ秒入力と個別通知UIを追加"
```

### Task 7: 回復・通知・移行を総合検証する

**Files:**
- Modify only if a test exposes a scoped defect.

- [ ] **Step 1: 全単体テストを実行する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore
```

Expected: 既存408件と追加テストがすべてPASS、失敗0。

- [ ] **Step 2: x64 Debugビルドを実行する**

```powershell
.\BuildAndRun.ps1 .\StaminaManager\StaminaManager.csproj -SkipRun
```

Expected: warning 0、error 0。

- [ ] **Step 3: 通知統合テストを実行する**

アプリを `-Detach` で起動し、出力PIDを使用する。

```powershell
.\tests\ui\task11-notification-integration.ps1 -AppPid <PID>
```

Expected: 個別ON/OFF、全体OFF、取消、再有効化、`GamesChanged`、設定復元がPASS。
テスト後に元データとWindows予約が復元される。

- [ ] **Step 4: 秒表示の画面状態を確認する**

8分30秒のゲームをテストデータで作り、OverviewとCompactで1秒ごとに表示が減ることを
3秒以上観察する。`winapp ui get-value` とスクリーンショットの両方で確認する。

- [ ] **Step 5: 差分とユーザー変更を確認する**

```powershell
git status --short
git diff --check -- . ':(exclude)StaminaManager/MainWindow.xaml'
git diff --check -- StaminaManager/MainWindow.xaml
git diff -- StaminaManager/MainPage.xaml.cs StaminaManager/MainWindow.xaml StaminaManager/MainWindow.xaml.cs StaminaManager/Views/SettingsPage.xaml.cs
```

Expected: 除外側は空、ユーザー所有 `MainWindow.xaml:20` 側だけは着手前から存在する
`trailing whitespace` 1件を報告する。ユーザーの既存UI変更が維持され、既知baseline以外の
空白エラーと意図しないファイル削除がない。

- [ ] **Step 6: 失敗があれば完了扱いにせず新しいRED/GREEN Taskへ戻す**

この検証Taskではコードを変更・コミットしない。失敗が見つかった場合は、失敗を再現する
最小テストと対象ファイルを明記したTaskを本計画へ追加し、RED確認、最小修正、GREEN確認、
対象限定stage、コミットまでを実行してからStep 1へ戻る。
