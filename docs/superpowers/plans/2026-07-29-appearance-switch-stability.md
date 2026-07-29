# Appearance Switch Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** SettingsとOverviewを往復しながらLight/Darkと5種類のバックドロップを切り替えても、入力同期COM呼び出しによるネイティブクラッシュを発生させない。

**Architecture:** 外観変更をUI入力イベントから `DispatcherQueue` の次ターンへ送るテスト可能なキューを追加し、既存ViewModelのセマフォで永続化順を維持する。タイトルバー色更新は同じキュー上で集約し、実行時点の最新テーマだけを適用する。バックドロップ実装と既存フォールバックは変更しない。

**Tech Stack:** C# 14、.NET 10、WinUI 3、Windows App SDK 2.3.1、WinUIEx 2.9.0、MSTest、winapp CLI UI Automation

---

## 実行場所と共通ルール

- Worktree: `D:\Programming\CSharp\StaminaManager\.worktrees\initial-implementation`
- Branch: `codex/feature/initial-implementation`
- Spec: `docs/superpowers/specs/2026-07-29-recovery-seconds-per-game-notifications-appearance-stability-design.md`
- 回復秒・個別通知計画がGREENになった後に実行する。
- `@superpowers:systematic-debugging` と `@superpowers:test-driven-development` を適用する。
- UI作業では `@winui-design`、起動確認では `@winui-dev-workflow`、UI試験では
  `@winui-ui-testing` を適用する。
- 各Task開始前に `git status --short`、`git log --oneline -5`、対象ファイルの
  `git diff` を確認する。
- ファイル編集には `apply_patch` を使用する。
- コミットはTask単位、Conventional Commits、日本語subjectとする。

## ユーザー所有の未コミット変更

本計画は次のユーザー変更と重なる。編集前後に必ず差分を読み、維持する。

```text
StaminaManager/MainPage.xaml.cs
  GameEditorDialog.RequestedTheme = ActualTheme
StaminaManager/MainWindow.xaml
  BlurTintOverlay
StaminaManager/MainWindow.xaml.cs
  BlurTintOverlayControl、タイトルバー色、Blur表示判定
StaminaManager/Views/SettingsPage.xaml.cs
  バックアップ確認ダイアログのRequestedTheme
```

`MainWindow.xaml.cs` と `SettingsPage.xaml.cs` は直接変更するが、上記コードを削除・復元・
整形し直してはならない。ユーザーは2026-07-29に、タイトルバー色処理全体を今回の
クラッシュ修正コミットへ含めることを明示承認した。したがってTask 4ではタイトルバー色の
追加hunk全体をstageしてよいが、Blur overlayとBackdrop判定のhunkは引き続きstageしない。

## ファイル構成

### 新規作成

```text
StaminaManager/Infrastructure/Windows/UiWorkQueue.cs
    IUiWorkQueue、DispatcherQueueUiWorkQueue、CoalescingUiAction
StaminaManager/Views/DeferredSettingsChangeExecutor.cs
    外観設定を入力イベント終了後へ送る
StaminaManager/Views/SettingsAppearanceChangeRouter.cs
    SettingsPageから具体値を受けて遅延executorへ渡す
StaminaManager.Tests/Infrastructure/Windows/UiWorkQueueTests.cs
StaminaManager.Tests/Views/SettingsAppearanceChangeRouterTests.cs
StaminaManager.Tests/Views/SettingsPageAppearanceRoutingContractTests.cs
tests/ui/appearance-navigation-stress.ps1
```

### 変更

```text
StaminaManager/Views/SettingsPage.xaml.cs
StaminaManager/MainWindow.xaml.cs
StaminaManager.Tests/Views/SettingsChangeExecutorTests.cs
tests/ui/README.md
```

### Task 1: ページ往復を含むクラッシュ回帰テストを固定する

**Files:**
- Create: `tests/ui/appearance-navigation-stress.ps1`
- Modify: `tests/ui/README.md`

- [ ] **Step 1: 現在のユーザー差分とクラッシュログを再確認する**

```powershell
git diff -- StaminaManager/MainWindow.xaml StaminaManager/MainWindow.xaml.cs StaminaManager/Views/SettingsPage.xaml.cs
Get-Content -LiteralPath 'C:\Users\saica\AppData\Local\Temp\winapp-dumps\debug-324-20260729-042808.log'
```

Expected: ユーザーのタイトルバー色処理を確認し、ログ末尾に
`Microsoft.UI.Xaml.dll ... E_INVALIDARG` がある。

- [ ] **Step 2: ストレスUIテストを書く**

スクリプトは`AppPid`を必須引数とし、初期テーマ・背景を保存する。次を最低3周行う。

```powershell
foreach ($backdrop in @('Mica', 'Acrylic', 'Blur', 'Transparent', 'Solid')) {
    Invoke-WinApp ui invoke ThemeToggle -a $AppPid | Out-Null
    Select-ComboItem BackdropSelector $backdrop
    Invoke-WinApp ui invoke NavOverview -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for OverviewScrollViewer -a $AppPid -t 3000 | Out-Null
    Invoke-WinApp ui invoke NavSettings -a $AppPid | Out-Null
    Invoke-WinApp ui wait-for BackdropSelector -a $AppPid -t 3000 | Out-Null
    if (-not (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
        throw "StaminaManager exited while switching $backdrop."
    }
}
```

ComboBox popup待機、プロセス生存確認、実際のバックドロップ診断確認を含める。成功時も
`finally`で初期テーマ・背景へ戻す。プロセスが落ちた場合は復元不能であることを結果へ
明記し、次回起動時に保存済み初期値へ戻せる情報を出力する。

- [ ] **Step 3: デバッグ出力付き現行ビルドでREDを確認する**

Terminal A:

```powershell
.\BuildAndRun.ps1 .\StaminaManager\StaminaManager.csproj
```

起動出力のPIDをTerminal Bで使用する。

```powershell
.\tests\ui\appearance-navigation-stress.ps1 -AppPid <PID>
```

Expected: 反復中または直後にプロセスが終了しテストFAIL。生成ログに
`0x8001010D` または `E_INVALIDARG` が記録される。1回で再現しない場合は最大3回まで
新しいプロセスで実行し、再現ログを保存する。3回とも生存した場合はテストPASSを
「今回の環境では非再現」と記録し、クラッシュ再現を偽装しない。Task 3の配線契約テストを
決定論的なREDとして使用するため、ここで作業を止めない。

- [ ] **Step 4: READMEへ再現目的と実行方法を記録する**

このテストが `Settings → Overview → Settings`、Light/Dark、5背景、遅延クラッシュを
対象とすることを明記する。このTaskではプロダクションコードを変更しない。

### Task 2: テスト可能なUI作業キューを追加する

**Files:**
- Create: `StaminaManager/Infrastructure/Windows/UiWorkQueue.cs`
- Create: `StaminaManager/Views/DeferredSettingsChangeExecutor.cs`
- Create: `StaminaManager.Tests/Infrastructure/Windows/UiWorkQueueTests.cs`
- Modify: `StaminaManager.Tests/Views/SettingsChangeExecutorTests.cs`

- [ ] **Step 1: キュー投入・失敗・順序の失敗テストを書く**

フェイクキューはActionを実行せず保持できるようにする。テストする内容:

- `ExecuteAsync` 呼び出し直後は設定変更が実行されていない。
- キューActionを実行すると設定変更、同期、Task完了の順になる。
- `TryEnqueue=false` では失敗報告とコントロール同期を各1回呼び、設定変更は実行しない。
- 2件のqueued Actionを開始しても、1件目の非同期変更が完了するまで2件目は変更処理へ
  入らず、1件目完了後に投入順で実行される。

```csharp
[TestMethod]
public async Task ExecuteAsync_DefersChangeUntilQueuedActionRuns()
{
    RecordingUiWorkQueue queue = new();
    List<string> calls = [];
    DeferredSettingsChangeExecutor executor = new(queue);

    Task pending = executor.ExecuteAsync(
        () => { calls.Add("change"); return Task.CompletedTask; },
        () => calls.Add("failure"),
        () => calls.Add("sync"));

    Assert.IsEmpty(calls);
    queue.RunNext();
    await pending;
    CollectionAssert.AreEqual(new[] { "change", "sync" }, calls);
}
```

- [ ] **Step 2: REDを確認する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SettingsChangeExecutorTests|FullyQualifiedName~UiWorkQueueTests"
```

Expected: 新しい型が存在せずコンパイルFAIL。

- [ ] **Step 3: 最小キュー抽象を実装する**

```csharp
internal interface IUiWorkQueue
{
    bool TryEnqueue(Action action);
}

internal sealed class DispatcherQueueUiWorkQueue(
    DispatcherQueue dispatcherQueue) : IUiWorkQueue
{
    public bool TryEnqueue(Action action) =>
        dispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Normal,
            new DispatcherQueueHandler(action));
}
```

`DeferredSettingsChangeExecutor.ExecuteAsync` は `TaskCompletionSource` を
`RunContinuationsAsynchronously` で作り、キューAction内で既存
`SettingsChangeExecutor.ExecuteAsync` をawaitする。投入失敗時は失敗報告と同期を行い、
Taskを正常完了させる。ユーザーへは既存InfoBar文言で失敗を伝える。

executor内部に専用 `SemaphoreSlim(1, 1)` を持ち、DispatcherQueue上で開始された各変更を
完了単位で直列化する。1件目を未完了 `TaskCompletionSource` で停止させ、2件目のqueued
Actionも開始した状態で2件目delegateが未実行であることをテストし、1件目解放後に
`first → second` の順で完了することを確認する。ViewModel側の既存gateはそのまま維持する。

- [ ] **Step 4: 対象テストをGREENにする**

Run: Step 2と同じ。

Expected: 遅延、投入失敗、FIFOがPASS。

- [ ] **Step 5: コミットする**

```powershell
git add -- StaminaManager/Infrastructure/Windows/UiWorkQueue.cs StaminaManager/Views/DeferredSettingsChangeExecutor.cs StaminaManager.Tests/Infrastructure/Windows/UiWorkQueueTests.cs StaminaManager.Tests/Views/SettingsChangeExecutorTests.cs tests/ui/appearance-navigation-stress.ps1 tests/ui/README.md
git commit -m "feat: 外観変更の遅延キューを追加"
```

### Task 3: Settingsの外観値をイベント時に確定して入力イベント外へ送る

**Files:**
- Create: `StaminaManager/Views/SettingsAppearanceChangeRouter.cs`
- Modify: `StaminaManager/Views/SettingsPage.xaml.cs`
- Test: `StaminaManager.Tests/Views/SettingsChangeExecutorTests.cs`
- Create: `StaminaManager.Tests/Views/SettingsAppearanceChangeRouterTests.cs`
- Create: `StaminaManager.Tests/Views/SettingsPageAppearanceRoutingContractTests.cs`

- [ ] **Step 1: 要求値を投入時に固定する失敗テストを追加する**

`DeferredSettingsChangeExecutor.ExecuteAsync<T>` へ異なる2値を渡してからフェイクキューを
実行し、遅延delegateがそれぞれ投入時の値を受け取るテストを追加する。UIコントロールを
delegate実行時に読み直す実装では、2件とも最後の値になるため、このテストで防ぐ。

```csharp
[TestMethod]
public async Task ExecuteAsync_CapturesEachRequestedValueBeforeQueueRuns()
{
    RecordingUiWorkQueue queue = new();
    List<AppTheme> applied = [];
    DeferredSettingsChangeExecutor executor = new(queue);

    Task first = executor.ExecuteAsync(
        AppTheme.Dark,
        theme => { applied.Add(theme); return Task.CompletedTask; },
        () => { },
        () => { });
    Task second = executor.ExecuteAsync(
        AppTheme.Light,
        theme => { applied.Add(theme); return Task.CompletedTask; },
        () => { },
        () => { });

    queue.RunNext();
    queue.RunNext();
    await Task.WhenAll(first, second);
    CollectionAssert.AreEqual(
        new[] { AppTheme.Dark, AppTheme.Light },
        applied);
}
```

`SettingsAppearanceChangeRouterTests` はテーマと背景の異なる2要求、投入失敗時の
`reportFailure` / `synchronizeControls` 各1回を検証する。

`SettingsPageAppearanceRoutingContractTests` は既存のソース契約テストと同じ方法で
`SettingsPage.xaml.cs` を読み、次を必須にする。

- イベント内で `AppTheme requestedTheme` と `BackdropKind requestedBackdrop` を作る。
- `_appearanceChangeRouter.ChangeThemeAsync(requestedTheme)` と
  `_appearanceChangeRouter.ChangeBackdropAsync(requestedBackdrop)` を呼ぶ。
- router構築時に `ViewModel.ReportUnexpectedFailure` と `SynchronizeControls` を渡す。
- 外観イベントのqueued delegate内で `ThemeToggle.IsOn` / `SelectedIndex` を読み直さない。

- [ ] **Step 2: REDを確認する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SettingsChangeExecutorTests|FullyQualifiedName~SettingsAppearanceChangeRouterTests|FullyQualifiedName~SettingsPageAppearanceRoutingContractTests"
```

Expected: 値付きoverload/routerが存在せずコンパイルFAILし、現行SettingsPageの直接配線も
契約違反になる。

- [ ] **Step 3: テーマと背景イベントだけを遅延executorへ切り替える**

`InitializeComponent` 後に現在の `DispatcherQueue` からexecutorを作成する。

```csharp
DeferredSettingsChangeExecutor executor = new(
    new DispatcherQueueUiWorkQueue(DispatcherQueue));
_appearanceChangeRouter = new SettingsAppearanceChangeRouter(
    executor,
    theme => ViewModel.SetThemeAsync(theme),
    backdrop => ViewModel.SetBackdropAsync(backdrop),
    ViewModel.ReportUnexpectedFailure,
    SynchronizeControls);
```

値付きoverloadは要求値を引数として受け、キュー投入前に閉包へ固定する。

```csharp
internal Task ExecuteAsync<T>(
    T requestedValue,
    Func<T, Task> settingChange,
    Action reportFailure,
    Action synchronizeControls) =>
    ExecuteAsync(
        () => settingChange(requestedValue),
        reportFailure,
        synchronizeControls);
```

`ThemeToggle_Toggled` と `BackdropSelector_SelectionChanged` は、コントロール値をawaitや
キュー投入より前にローカルへ取り、次の形にする。

```csharp
AppTheme requestedTheme = ThemeToggle.IsOn
    ? AppTheme.Dark
    : AppTheme.Light;
await _appearanceChangeRouter.ChangeThemeAsync(requestedTheme);
```

背景も `BackdropKind requestedBackdrop = (BackdropKind)BackdropSelector.SelectedIndex;` を
先に確定し、`ChangeBackdropAsync(requestedBackdrop)` へ渡す。routerは値付きoverloadを使い、
queued delegate内ではUIコントロールを読まない。

CloseBehavior、Startup、Notifications、通知分数は既存executorを維持する。
バックアップ確認ダイアログの `RequestedTheme = ActualTheme` を必ず保持する。

- [ ] **Step 4: 単体テストをGREENにする**

Run: Step 2と同じ。

Expected: 連続変更、投入失敗、最終同期がPASS。

- [ ] **Step 5: コミットする**

```powershell
git add -- StaminaManager/Views/SettingsAppearanceChangeRouter.cs StaminaManager.Tests/Views/SettingsChangeExecutorTests.cs StaminaManager.Tests/Views/SettingsAppearanceChangeRouterTests.cs StaminaManager.Tests/Views/SettingsPageAppearanceRoutingContractTests.cs
git add -p -- StaminaManager/Views/SettingsPage.xaml.cs
git diff --cached -- StaminaManager/Views/SettingsPage.xaml.cs
git diff -- StaminaManager/Views/SettingsPage.xaml.cs
git commit -m "fix: 外観変更を入力イベント後へ遅延"
```

対話stageでは、今回追加したfield、constructor、テーマ・背景handlerのhunkだけを選ぶ。
ユーザー所有のバックアップ確認ダイアログ `RequestedTheme` hunkはstageせず、作業ツリーへ
残す。差分確認でその行が未stage側に残り、コード自体は削除されていないことを確認する。

### Task 4: タイトルバー色更新を集約する

**Files:**
- Modify: `StaminaManager/Infrastructure/Windows/UiWorkQueue.cs`
- Modify: `StaminaManager/MainWindow.xaml.cs`
- Test: `StaminaManager.Tests/Infrastructure/Windows/UiWorkQueueTests.cs`

- [ ] **Step 1: 集約処理の失敗テストを書く**

追加するテスト:

- 3回`Request`してもキューActionは1件。
- Action実行後は次の`Request`を新規投入できる。
- `TryEnqueue=false` の場合は保留状態を解除して再試行できる。
- 実行Actionは要求時ではなく実行時の最新テーマ値を読む。

- [ ] **Step 2: REDを確認する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~UiWorkQueueTests"
```

Expected: `CoalescingUiAction` が存在せずFAIL。

- [ ] **Step 3: CoalescingUiActionを最小実装する**

```csharp
internal sealed class CoalescingUiAction(
    IUiWorkQueue queue,
    Action action)
{
    private bool _isQueued;

    public bool Request()
    {
        if (_isQueued)
        {
            return true;
        }

        _isQueued = true;
        if (queue.TryEnqueue(() =>
        {
            _isQueued = false;
            action();
        }))
        {
            return true;
        }

        _isQueued = false;
        return false;
    }
}
```

UIスレッド専用として使用し、不要なロックを追加しない。

- [ ] **Step 4: MainWindowのActualThemeChangedをキューへ接続する**

`MainWindow` へreadonly fieldを追加し、`_windowContent` と `Content` の初期化後に作る。

```csharp
private readonly CoalescingUiAction _captionColorUpdate;

_captionColorUpdate = new CoalescingUiAction(
    new DispatcherQueueUiWorkQueue(DispatcherQueue),
    ApplyCaptionButtonColors);
_windowContent.ActualThemeChanged += OnActualThemeChanged;
ApplyCaptionButtonColors();
```

既存の匿名ラムダを次の名前付きハンドラーへ置き換える。

```csharp
private void OnActualThemeChanged(
    FrameworkElement sender,
    object args)
{
    if (!_captionColorUpdate.Request())
    {
        Debug.WriteLine("Caption color update could not be queued.");
    }
}
```

`ApplyCaptionButtonColors` は実行時の `_windowContent.ActualTheme` を読み、既存の色値を
そのまま使用する。`COMException`、`ArgumentException`、`InvalidOperationException` は
診断出力へ記録して終了する。コンストラクターの初回同期適用は維持する。

`BlurTintOverlayControl`、`SetBackdrop`内のBlur判定、ユーザーの配色値を変更しない。

- [ ] **Step 5: 対象テストをGREENにする**

Run: Step 2と同じ。

Expected: 集約、再試行、最新値がPASS。

- [ ] **Step 6: コミットする**

```powershell
git add -- StaminaManager/Infrastructure/Windows/UiWorkQueue.cs StaminaManager.Tests/Infrastructure/Windows/UiWorkQueueTests.cs
git add -p -- StaminaManager/MainWindow.xaml.cs
git diff --cached -- StaminaManager/MainWindow.xaml.cs
git diff -- StaminaManager/MainWindow.xaml.cs
git commit -m "fix: タイトルバー色更新を安全に集約"
```

対話stageでは、明示承認済みのタイトルバー色全体・キュー接続と必要なusing/fieldを選ぶ。
`BlurTintOverlayControl` と `SetBackdrop` のBlur表示判定はユーザー所有hunkとして未stageに
残す。タイトルバー色コードは今回の修正対象そのものなので、修正後のhunkをstageする。

### Task 5: クラッシュ回帰と全外観を検証する

**Files:**
- Modify only if a test exposes a scoped defect.

- [ ] **Step 1: 対象単体テストを実行する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~SettingsChangeExecutorTests|FullyQualifiedName~SettingsAppearanceChangeRouterTests|FullyQualifiedName~SettingsPageAppearanceRoutingContractTests|FullyQualifiedName~UiWorkQueueTests|FullyQualifiedName~AppearanceServiceTests|FullyQualifiedName~SettingsViewModelTests"
```

Expected: 全対象PASS。

- [ ] **Step 2: デバッグ出力付きで起動する**

Terminal A:

```powershell
.\BuildAndRun.ps1 .\StaminaManager\StaminaManager.csproj
```

Expected: x64 Debug build warning 0、error 0、起動PIDが表示される。

- [ ] **Step 3: ページ往復ストレステストを3回実行する**

Terminal B:

```powershell
1..3 | ForEach-Object {
    .\tests\ui\appearance-navigation-stress.ps1 -AppPid <PID>
}
```

Expected: 3回ともPASS、プロセス生存、初期設定へ復元。デバッグログ末尾に新しい
`E_INVALIDARG` / C++ fail-fastがない。

- [ ] **Step 4: 既存外観統合テストを実行する**

```powershell
.\tests\ui\task9-appearance-integration.ps1 -AppPid <PID> -OutputDirectory "$env:TEMP\StaminaManager-appearance-fixed"
```

Expected: Mica、Acrylic、Blur、Transparent、SolidとLight/DarkがPASS。既存フォーカス検証が
環境依存で失敗した場合は、外観・プロセス生存の結果と分離して報告し、要求外の変更を
混ぜない。

- [ ] **Step 5: スクリーンショットを目視する**

```powershell
winapp ui screenshot -a <PID> -o "$env:TEMP\StaminaManager-appearance-fixed\final.png"
```

確認項目: タイトルバー文字色、キャプションボタンhover/pressed、Blur tint、
Transparent、Light/Dark、意図しない単色化がない。

- [ ] **Step 6: 全単体テストと差分検査を実行する**

```powershell
dotnet test .\StaminaManager.Tests\StaminaManager.Tests.csproj -c Debug --no-restore
git diff --check -- . ':(exclude)StaminaManager/MainWindow.xaml'
git diff --check -- StaminaManager/MainWindow.xaml
git status --short
```

Expected: 全テストPASS。除外側に空白エラーなし。ユーザー所有 `MainWindow.xaml:20` 側だけは
着手前から存在する `trailing whitespace` 1件を報告し、それ以外のユーザー変更が維持される。

- [ ] **Step 7: 失敗があれば完了扱いにせず新しいRED/GREEN Taskへ戻す**

この検証Taskではコードを変更・コミットしない。失敗が見つかった場合は、失敗を再現する
最小テストと対象ファイルを明記したTaskを本計画へ追加し、RED確認、最小修正、GREEN確認、
対象限定stage、コミットまでを実行してからStep 1へ戻る。
