# StaminaManager Initial Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Windows 11 x64向けに、自由登録型のスタミナ管理、コンパクト表示、予約通知、タスクトレイ、バックアップ、5種のバックドロップを備えたMicrosoft Store提出可能なWinUI 3アプリを構築する。

**Architecture:** 3プロジェクト構成とし、`StaminaManager.Core`に時刻非依存のモデル・計算・検証・ポート、`StaminaManager`にPresentation/Application/Infrastructure、`StaminaManager.Tests`に単体・統合テストを置く。Windows依存機能はCoreのインターフェース越しに注入し、UIはMVVMで構成する。

**Tech Stack:** C# 14、.NET 10、WinUI 3、Windows App SDK 2.3.1、CommunityToolkit.Mvvm 8.4.2、WinUIEx 2.9.0、System.Text.Json、MSTest、MSIX、winapp CLI 0.5.0

---

## 実行場所と共通ルール

- Worktree: `D:\Programming\CSharp\StaminaManager\.worktrees\initial-implementation`
- Branch: `codex/feature/initial-implementation`
- Spec: `docs/superpowers/specs/2026-07-25-stamina-manager-design.md`
- Design tokens: `DESIGN.md`
- 各Task開始前に `git status --short` と `git log --oneline -5` を確認する。
- ロジック実装は `@superpowers:test-driven-development` に従い、失敗テスト→最小実装→成功テストの順にする。
- WinUI作業では `@winui-design`、起動確認では `@winui-dev-workflow`、UI自動試験では `@winui-ui-testing`、MSIX生成では `@winui-packaging` を適用する。
- パッケージ化した実行ファイルを直接起動せず、`BuildAndRun.ps1` または `winapp run` を使用する。
- Platformは常に`x64`とし、`AnyCPU`、unpackaged、`WindowsPackageType=None`を使用しない。
- ファイルの追加・編集は`apply_patch`を使用する。テンプレートが生成した不要物は即削除せず、利用形へ変更するか、削除が必要なら対象をユーザーへ列挙して確認する。
- コミットはTask単位、Conventional Commits、日本語subjectとする。

## ファイル構成

```text
StaminaManager.slnx
global.json
Directory.Build.props
Directory.Packages.props
BuildAndRun.ps1
ThirdPartyNotices.txt
StaminaManager/
├─ App.xaml / App.xaml.cs
├─ MainWindow.xaml / MainWindow.xaml.cs
├─ MainPage.xaml / MainPage.xaml.cs              # NavigationView shell
├─ StaminaManager.csproj
├─ Package.appxmanifest
├─ Application/
│  ├─ AppCoordinator.cs
│  ├─ GameManager.cs
│  ├─ TimerCoordinator.cs
│  └─ RestoreCoordinator.cs
├─ Controls/
│  ├─ GameCardControl.xaml / .cs
│  ├─ StaminaRing.xaml / .cs
│  └─ GameEditorDialog.xaml / .cs
├─ Infrastructure/
│  ├─ Backup/BackupCoordinator.cs
│  ├─ Backup/BackupManifest.cs
│  ├─ Backup/SafeZipReader.cs
│  ├─ Notifications/NotificationLedgerStore.cs
│  ├─ Notifications/WindowsNotificationScheduler.cs
│  ├─ Persistence/AppDataPathProvider.cs
│  ├─ Persistence/LocalDataStore.cs
│  ├─ Persistence/JsonSerializationContext.cs
│  ├─ Storage/AssetStore.cs
│  ├─ Windows/BackdropService.cs
│  ├─ Windows/BlurredBackdrop.cs
│  ├─ Windows/StartupService.cs
│  ├─ Windows/TrayService.cs
│  └─ Windows/WindowStateService.cs
├─ Resources/
│  ├─ DesignTokens.xaml
│  ├─ Styles.xaml
│  └─ Strings/ja-JP/Resources.resw
├─ ViewModels/
│  ├─ ShellViewModel.cs
│  ├─ OverviewViewModel.cs
│  ├─ GameCardViewModel.cs
│  ├─ CompactViewModel.cs
│  ├─ GameEditorViewModel.cs
│  └─ SettingsViewModel.cs
├─ Views/
│  ├─ OverviewPage.xaml / .cs
│  ├─ CompactPage.xaml / .cs
│  └─ SettingsPage.xaml / .cs
└─ Assets/
   ├─ Brand/
   └─ FallbackGame.png
StaminaManager.Core/
├─ Models/GameEntry.cs
├─ Models/AppSettings.cs
├─ Models/Enums.cs
├─ Calculations/StaminaCalculator.cs
├─ Calculations/StaminaSnapshot.cs
├─ Validation/GameEntryValidator.cs
├─ Validation/GameEditPolicy.cs
├─ Validation/BackupLimits.cs
├─ Abstractions/IClock.cs
├─ Abstractions/ILocalDataStore.cs
├─ Abstractions/INotificationScheduler.cs
├─ Abstractions/INotificationPermissionService.cs
├─ Abstractions/ISettingsLauncher.cs
├─ Abstractions/IStartupService.cs
├─ Abstractions/IBackdropService.cs
├─ Abstractions/IThemeService.cs
├─ Abstractions/IWindowStateService.cs
├─ Abstractions/ITrayService.cs
├─ Abstractions/IBackupService.cs
└─ Persistence/DataEnvelope.cs
StaminaManager.Tests/
├─ Calculations/StaminaCalculatorTests.cs
├─ Validation/GameEntryValidatorTests.cs
├─ Validation/GameEditPolicyTests.cs
├─ Persistence/LocalDataStoreTests.cs
├─ Application/GameManagerTests.cs
├─ Notifications/NotificationStateMachineTests.cs
├─ Backup/SafeZipReaderTests.cs
├─ Backup/BackupCoordinatorTests.cs
└─ TestDoubles/FakeClock.cs
tests/ui/StaminaManager.UiTests.ps1
```

### Task 1: ソリューションと再現可能なビルド基盤

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `StaminaManager.slnx`
- Create: `StaminaManager/StaminaManager.csproj` and WinUI template files
- Create: `StaminaManager.Core/StaminaManager.Core.csproj`
- Create: `StaminaManager.Tests/StaminaManager.Tests.csproj`
- Create: `BuildAndRun.ps1`
- Create: `ThirdPartyNotices.txt`
- Modify: `.gitignore`

- [ ] **Step 1: テンプレートの生成内容をdry-runで確認する**

Run:

```powershell
dotnet new winui --name StaminaManager --output StaminaManager --dotnet-version net10.0 --target-platform-min-version 10.0.22000.0 --UseLatestWindowsAppSDK false --windowsAppSdkVersion 2.3.1 --windowsSdkBuildToolsVersion 10.0.26100.7705 --windowsSdkBuildToolsWinAppVersion 0.3.1 --dry-run
```

Expected: `StaminaManager.csproj`、`Package.appxmanifest`、`MainWindow`、`MainPage`、MSIX画像が作成対象として列挙され、既存ファイルの上書きがない。

- [ ] **Step 2: 3プロジェクトを生成してslnxへ登録する**

Run:

```powershell
dotnet new winui --name StaminaManager --output StaminaManager --dotnet-version net10.0 --target-platform-min-version 10.0.22000.0 --UseLatestWindowsAppSDK false --windowsAppSdkVersion 2.3.1 --windowsSdkBuildToolsVersion 10.0.26100.7705 --windowsSdkBuildToolsWinAppVersion 0.3.1
dotnet new classlib --name StaminaManager.Core --output StaminaManager.Core --framework net10.0
dotnet new mstest --name StaminaManager.Tests --output StaminaManager.Tests --framework net10.0
dotnet new sln --name StaminaManager --format slnx
dotnet sln StaminaManager.slnx add StaminaManager/StaminaManager.csproj StaminaManager.Core/StaminaManager.Core.csproj StaminaManager.Tests/StaminaManager.Tests.csproj
dotnet add StaminaManager/StaminaManager.csproj reference StaminaManager.Core/StaminaManager.Core.csproj
dotnet add StaminaManager.Tests/StaminaManager.Tests.csproj reference StaminaManager.Core/StaminaManager.Core.csproj StaminaManager/StaminaManager.csproj
```

Expected: `StaminaManager.slnx`に3プロジェクトだけが登録される。

- [ ] **Step 3: SDK・Platform・NuGetバージョンを中央固定する**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestPatch"
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props` and remove package-level `Version` attributes from project files:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.WindowsAppSDK" Version="2.3.1" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.7705" />
    <PackageVersion Include="Microsoft.Windows.SDK.BuildTools.WinApp" Version="0.3.1" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="WinUIEx" Version="2.9.0" />
  </ItemGroup>
</Project>
```

Keep MSTest package versions emitted by the installed .NET 10.0.302 template, move those exact versions into `Directory.Packages.props`, and add versionless references to `StaminaManager.Tests.csproj`. Change the test project's `TargetFramework` to `net10.0-windows10.0.26100.0` before adding the app project reference. In the app project set `TargetFramework` to `net10.0-windows10.0.26100.0`, `TargetPlatformMinVersion` to `10.0.22000.0`, `RuntimeIdentifier` to `win-x64`, and `Platforms` to `x64`. Add versionless `CommunityToolkit.Mvvm` and `WinUIEx` references only to the app project.

- [ ] **Step 4: 開発スクリプトと追跡除外を整える**

Reuse `C:\Users\saica\.codex\skills\winui-dev-workflow\BuildAndRun.ps1` as root `BuildAndRun.ps1`. Append these patterns to root `.gitignore`:

```gitignore
*.pfx
*.cer
*.msix
*.msixupload
AppX/
artifacts/
tests/ui/results/
```

Create `ThirdPartyNotices.txt` containing package name, version, project URL, and MIT license attribution for WinUIEx and CommunityToolkit.Mvvm.

- [ ] **Step 5: 最初のrestore・test・buildを実行する**

Run:

```powershell
dotnet restore StaminaManager.slnx
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj -c Debug -p:Platform=x64
.\BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -SkipRun
```

Expected: restore成功、テンプレートテスト成功、x64 Debug build成功。

- [ ] **Step 6: 基盤をコミットする**

```powershell
git add .
git commit -m "chore: WinUIソリューションの基盤を構築"
```

### Task 2: スタミナ計算ドメイン

**Files:**
- Create: `StaminaManager.Core/Models/GameEntry.cs`
- Create: `StaminaManager.Core/Models/Enums.cs`
- Create: `StaminaManager.Core/Calculations/StaminaSnapshot.cs`
- Create: `StaminaManager.Core/Calculations/StaminaCalculator.cs`
- Create: `StaminaManager.Core/Abstractions/IClock.cs`
- Test: `StaminaManager.Tests/Calculations/StaminaCalculatorTests.cs`
- Test: `StaminaManager.Tests/TestDoubles/FakeClock.cs`

- [ ] **Step 1: 境界値を網羅する失敗テストを書く**

Tests must assert:

```csharp
[DataRow(49, 100, StaminaStatus.Safe)]
[DataRow(50, 100, StaminaStatus.Attention)]
[DataRow(80, 100, StaminaStatus.Attention)]
[DataRow(81, 100, StaminaStatus.NearFull)]
[DataRow(100, 100, StaminaStatus.Full)]
[DataRow(101, 100, StaminaStatus.OverCap)]
public void Calculate_UsesExactThresholds(int current, int maximum, StaminaStatus expected)
```

Also test elapsed recovery, fractional recovery truncation, future clock clamp, exact full time, base over cap preservation, ring ratio cap at 1.0, and `DateTimeOffset` overflow rejection.

- [ ] **Step 2: テストが型未定義で失敗することを確認する**

Run: `dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter StaminaCalculatorTests -p:Platform=x64`

Expected: FAIL because domain types do not exist.

- [ ] **Step 3: 最小のモデルと計算を実装する**

`GameEntry` is an immutable record with `Id`, `Name`, `BaseStamina`, `MaxStamina`, `RecoveryMinutes`, `RecordedAtUtc`, `ImageAssetId`, and `SortOrder`. `StaminaSnapshot` exposes `Current`, `Maximum`, `Ratio`, `Status`, `FullAtUtc`, and `Remaining`.

`StaminaCalculator.Calculate(GameEntry entry, DateTimeOffset nowUtc)` must:

```text
if BaseStamina >= MaxStamina: preserve BaseStamina and return Full/OverCap
elapsed = max(0, floor((nowUtc - RecordedAtUtc).TotalMinutes))
recovered = elapsed / RecoveryMinutes
current = min(MaxStamina, BaseStamina + recovered)
status uses long cross multiplication without percentage rounding
fullAt uses checked long multiplication and checked DateTimeOffset addition
```

- [ ] **Step 4: 対象テストと全テストを成功させる**

Run:

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter StaminaCalculatorTests -p:Platform=x64
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj -p:Platform=x64
```

Expected: all PASS.

- [ ] **Step 5: コミットする**

```powershell
git add StaminaManager.Core StaminaManager.Tests
git commit -m "feat: スタミナ計算ドメインを追加"
```

### Task 3: 入力検証と編集時刻ポリシー

**Files:**
- Create: `StaminaManager.Core/Validation/GameDraft.cs`
- Create: `StaminaManager.Core/Validation/ValidationResult.cs`
- Create: `StaminaManager.Core/Validation/GameEntryValidator.cs`
- Create: `StaminaManager.Core/Validation/GameEditPolicy.cs`
- Test: `StaminaManager.Tests/Validation/GameEntryValidatorTests.cs`
- Test: `StaminaManager.Tests/Validation/GameEditPolicyTests.cs`

- [ ] **Step 1: 数値・必須名・時刻範囲の失敗テストを書く**

Test empty/whitespace name, current `-1/1_000_001`, maximum `0/1_000_001`, recovery `0/525_601`, valid over-cap current, and calculated full time overflow. Assert field keys and Japanese correction messages, not only a boolean.

- [ ] **Step 2: メタデータ編集と回復条件編集の失敗テストを書く**

Assert `GameEditPolicy.Apply` preserves `BaseStamina/RecordedAtUtc` for name/image-only edits, and resets both to the draft current value/save clock when current/max/recovery changes.

- [ ] **Step 3: 失敗を確認する**

Run: `dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "GameEntryValidatorTests|GameEditPolicyTests" -p:Platform=x64`

Expected: FAIL because validators do not exist.

- [ ] **Step 4: 検証と編集ポリシーを実装する**

Use named constants `MaxStaminaValue = 1_000_000`, `MaxRecoveryMinutes = 525_600`, and `MaxGameCount = 100`. Return immutable field errors. Do not throw for user-correctable input; throw only for programmer contract violations.

- [ ] **Step 5: テストを成功させてコミットする**

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "GameEntryValidatorTests|GameEditPolicyTests" -p:Platform=x64
git add StaminaManager.Core StaminaManager.Tests
git commit -m "feat: ゲーム入力と編集基準時刻を検証"
```

### Task 4: 設定モデル、JSON、アトミック保存、画像保管

**Files:**
- Create: `StaminaManager.Core/Models/AppSettings.cs`
- Create: `StaminaManager.Core/Persistence/DataEnvelope.cs`
- Create: `StaminaManager.Core/Abstractions/ILocalDataStore.cs`
- Create: `StaminaManager/Infrastructure/Persistence/AppDataPathProvider.cs`
- Create: `StaminaManager/Infrastructure/Persistence/JsonSerializationContext.cs`
- Create: `StaminaManager/Infrastructure/Persistence/LocalDataStore.cs`
- Create: `StaminaManager/Infrastructure/Storage/AssetStore.cs`
- Test: `StaminaManager.Tests/Persistence/LocalDataStoreTests.cs`
- Test: `StaminaManager.Tests/Persistence/AssetStoreTests.cs`

- [ ] **Step 1: JSON round-tripと回復スナップショットの失敗テストを書く**

Use a temporary directory provider. Test schema version `1`, stable camelCase JSON, UTC timestamps, initial save, replacement save, corrupt primary fallback to recovery snapshot, and preservation of corrupt input for diagnostics.

- [ ] **Step 2: 画像検証の失敗テストを書く**

Use tiny committed test fixtures under `StaminaManager.Tests/TestData/Images`. Assert valid PNG/JPEG, MIME/decode verification, 5 MiB limit, 4096×4096 limit, random asset name, and SVG/renamed-text rejection.

- [ ] **Step 3: 失敗を確認する**

Run: `dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "LocalDataStoreTests|AssetStoreTests" -p:Platform=x64`

- [ ] **Step 4: ソース生成JSONとアトミック保存を実装する**

`LocalDataStore.SaveAsync` writes `data.json.tmp`, flushes, rotates the previous valid file to `data.recovery.json`, then replaces `data.json`. `LoadAsync` never overwrites a corrupt file and returns a typed recovery result. Use `System.Text.Json` source generation; do not use reflection-based fallback.

- [ ] **Step 5: AssetStoreを実装する**

Decode before accepting; re-encode accepted images to an app-owned PNG/JPEG file; never reuse the user path. Delete orphaned app-owned assets only after a successful data commit.

- [ ] **Step 6: テストを成功させてコミットする**

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "LocalDataStoreTests|AssetStoreTests" -p:Platform=x64
git add StaminaManager.Core StaminaManager StaminaManager.Tests
git commit -m "feat: ローカルデータとゲーム画像を永続化"
```

### Task 5: GameManager、タイマー、ViewModel基盤

**Files:**
- Create: `StaminaManager/Application/AppCoordinator.cs`
- Create: `StaminaManager/Application/GameManager.cs`
- Create: `StaminaManager/Application/TimerCoordinator.cs`
- Create: `StaminaManager/ViewModels/GameCardViewModel.cs`
- Create: `StaminaManager/ViewModels/OverviewViewModel.cs`
- Create: `StaminaManager/ViewModels/ShellViewModel.cs`
- Create: `StaminaManager.Tests/Application/GameManagerTests.cs`
- Create: `StaminaManager.Tests/Application/TimerCoordinatorTests.cs`

- [ ] **Step 1: GameManagerの失敗テストを書く**

Test add/edit/delete, insertion order, sort order normalization, 100-game rejection, persistence failure keeping in-memory draft, and edit policy delegation.

- [ ] **Step 2: TimerCoordinatorの失敗テストを書く**

With `FakeClock`, assert immediate refresh, 30-second cadence while visible, stop while hidden, resume refresh, and cancellation on disposal. Do not use real sleeps; inject a tick source.

- [ ] **Step 3: 失敗を確認して最小実装を書く**

Run: `dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "GameManagerTests|TimerCoordinatorTests" -p:Platform=x64`

Implement application services and CommunityToolkit.Mvvm observable properties/commands. Create `AppCoordinator` as the application-facing facade for initial load, activation routing, mode restoration, and derived-service reconciliation; begin with only the services available in this Task. ViewModels may depend on Application services and Core models, never directly on Infrastructure.

- [ ] **Step 4: テストとbuildを成功させる**

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "GameManagerTests|TimerCoordinatorTests" -p:Platform=x64
.\BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -SkipRun
```

- [ ] **Step 5: コミットする**

```powershell
git add StaminaManager StaminaManager.Tests
git commit -m "feat: ゲーム管理と表示更新サービスを追加"
```

### Task 6: Fluentシェル、デザイントークン、ローカライズ

**Files:**
- Modify: `StaminaManager/App.xaml`
- Modify: `StaminaManager/App.xaml.cs`
- Modify: `StaminaManager/Application/AppCoordinator.cs`
- Modify: `StaminaManager/MainWindow.xaml`
- Modify: `StaminaManager/MainWindow.xaml.cs`
- Modify: `StaminaManager/MainPage.xaml`
- Modify: `StaminaManager/MainPage.xaml.cs`
- Create: `StaminaManager/Resources/DesignTokens.xaml`
- Create: `StaminaManager/Resources/Styles.xaml`
- Create: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- Create: `StaminaManager/Views/OverviewPage.xaml`
- Create: `StaminaManager/Views/OverviewPage.xaml.cs`
- Create: `StaminaManager/Views/SettingsPage.xaml`
- Create: `StaminaManager/Views/SettingsPage.xaml.cs`

- [ ] **Step 1: DESIGN.mdをXAMLリソースへ写像する**

Define semantic brushes for Accent, Safe, Attention, Urgent, CardSurface, PrimaryText, SecondaryText, and High Contrast overrides. Define spacing `4/8/12/16/20/24/32`, corner radius `8`, title `28`, card title `16`, body `14`, metadata `12`. Use `Segoe UI Variable` and explicit `Yu Gothic UI` fallback.

- [ ] **Step 2: MainPageをNavigationViewシェルとして実装する**

Only `Overview` and `Settings` items are visible. Use AutomationIds `NavOverview`, `NavSettings`, `ShellContentFrame`, and `CompactModeButton`. Keep native title-bar caption buttons and keyboard navigation.

- [ ] **Step 3: Composition RootをApp.xaml.csへ作る**

Construct Core/Application/Infrastructure services once, pass ViewModels into pages, and handle activation through one `AppCoordinator`. Register `AppCoordinator` from Task 5 and update its constructor explicitly whenever later Tasks add Backdrop, Window, Startup, Notification, or Backup ports. No service locator in ViewModels.

- [ ] **Step 4: x64 buildと起動確認を行う**

Run `./BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -Detach`.

Expected: packaged app launches, Japanese Overview/Settings navigation works, no About/Notifications navigation item appears, no first-chance exception is logged.

- [ ] **Step 5: コミットする**

```powershell
git add StaminaManager
git commit -m "feat: Fluentシェルとデザイン基盤を追加"
```

### Task 7: Overviewカード、円形計器、レスポンシブ列

**Files:**
- Create: `StaminaManager.Core/Calculations/OverviewLayoutPolicy.cs`
- Create: `StaminaManager/Controls/StaminaRing.xaml`
- Create: `StaminaManager/Controls/StaminaRing.xaml.cs`
- Create: `StaminaManager/Controls/GameCardControl.xaml`
- Create: `StaminaManager/Controls/GameCardControl.xaml.cs`
- Modify: `StaminaManager/Views/OverviewPage.xaml`
- Modify: `StaminaManager/Views/OverviewPage.xaml.cs`
- Modify: `StaminaManager/ViewModels/OverviewViewModel.cs`
- Test: `StaminaManager.Tests/Calculations/OverviewLayoutPolicyTests.cs`

- [ ] **Step 1: 719/720 pxと最小2列の失敗テストを書く**

```csharp
[DataRow(720, 3)]
[DataRow(719, 2)]
[DataRow(0, 2)]
public void GetColumns_UsesSpecifiedBreakpoints(double width, int expected)
```

- [ ] **Step 2: LayoutPolicyとリング幾何を実装する**

`StaminaRing` exposes dependency properties `Current`, `Maximum`, `Ratio`, `StatusText`, and `StatusBrush`; arc sweep is capped at 359.99 degrees for full state and never exceeds one revolution. AutomationProperties must expose game name, current, maximum, and status.

- [ ] **Step 3: カードグリッドと追加カードを実装する**

Use an `ItemsRepeater` with a `UniformGridLayout` whose `MaximumRowsOrColumns` and minimum item width are updated from `OverviewLayoutPolicy`. The ViewModel sequence always appends one `AddGameItem`; when games are empty it is index 0. AutomationIds: `OverviewItems`, `AddGameCard`, and `GameCard_{Guid}`.

- [ ] **Step 4: 状態と空・失敗表示を実装する**

Each card shows image/fallback, ring, game name, `current / maximum`, explicit status label, and rounded-up remaining time. Add `OverviewInfoBar` for save/notification/recovery errors. No state may rely on color alone.

- [ ] **Step 5: テスト、起動、3幅スクリーンショットを確認する**

Run unit tests, launch detached, resize to content widths 720/719 and the Standard minimum width,
and assert `3 / 2 / 2` columns. Capture screenshots using `winapp ui screenshot`, and inspect
for clipping/overlap.

- [x] **Step 6: コミットする**

```powershell
git add StaminaManager.Core StaminaManager StaminaManager.Tests
git commit -m "feat: スタミナカードのOverviewを実装"
```

### Task 8: 追加・編集・削除ダイアログとコンパクト表示

**Files:**
- Create: `StaminaManager/ViewModels/GameEditorViewModel.cs`
- Create: `StaminaManager/Controls/GameEditorDialog.xaml`
- Create: `StaminaManager/Controls/GameEditorDialog.xaml.cs`
- Create: `StaminaManager/ViewModels/CompactViewModel.cs`
- Create: `StaminaManager/Views/CompactPage.xaml`
- Create: `StaminaManager/Views/CompactPage.xaml.cs`
- Modify: `StaminaManager/ViewModels/ShellViewModel.cs`
- Modify: `StaminaManager/Application/AppCoordinator.cs`
- Modify: `StaminaManager.Core/Models/AppSettings.cs`
- Modify: `StaminaManager/MainPage.xaml.cs`
- Test: `StaminaManager.Tests/ViewModels/GameEditorViewModelTests.cs`
- Test: `StaminaManager.Tests/ViewModels/CompactViewModelTests.cs`

- [ ] **Step 1: ViewModelの失敗テストを書く**

Test inline field errors, save enabled state, metadata-only edit preservation, elapsed-during-dialog warning, delete-confirm substate preserving draft, selected-game fallback after deletion, empty compact state, and round-trip persistence of `LastDisplayMode` plus `SelectedCompactGameId`.

- [ ] **Step 2: 単一ContentDialogの状態遷移を実装する**

Use one dialog and switch between `Editing` and `DeleteConfirmation`; never call a second `ContentDialog.ShowAsync` while the first is open. AutomationIds: `GameNameInput`, `CurrentStaminaInput`, `MaxStaminaInput`, `RecoveryMinutesInput`, `ChooseGameImageButton`, `GameEditorSaveButton`, `GameEditorDeleteButton`, `DeleteConfirmButton`, `DeleteBackButton`.

- [ ] **Step 3: コンパクト表示を実装する**

Switch the same window to 420×520, enforce a 360×480 minimum, keep one-column content even if enlarged, and expose `CompactGameSelector`, `CompactUpdateButton`, `CompactEditButton`, `ReturnOverviewButton`. Restore normal bounds on return.

Persist `LastDisplayMode` and `SelectedCompactGameId` through `AppSettings` after a successful mode/selection change. At startup `AppCoordinator` restores compact mode only after data load; if the saved game no longer exists, select the first registered game, and if none exists show the compact empty/add state.

- [ ] **Step 4: テストと起動確認を行う**

Run ViewModel tests and use `BuildAndRun.ps1 -Detach`. Manually verify add→edit→delete-back→save, then compact→update→return without nested dialog exceptions.

- [ ] **Step 5: コミットする**

```powershell
git add StaminaManager.Core StaminaManager StaminaManager.Tests
git commit -m "feat: ゲーム編集とコンパクト表示を追加"
```

### Task 9: Appearance・General・Notifications・Data設定UI

**Files:**
- Modify: `StaminaManager.Core/Models/AppSettings.cs`
- Create: `StaminaManager/ViewModels/SettingsViewModel.cs`
- Modify: `StaminaManager/Views/SettingsPage.xaml`
- Modify: `StaminaManager/Views/SettingsPage.xaml.cs`
- Create: `StaminaManager/Infrastructure/Windows/BackdropService.cs`
- Create: `StaminaManager/Infrastructure/Windows/BlurredBackdrop.cs`
- Create: `StaminaManager/Infrastructure/Windows/ThemeService.cs`
- Create: `StaminaManager.Core/Abstractions/IBackdropService.cs`
- Create: `StaminaManager.Core/Abstractions/IThemeService.cs`
- Modify: `StaminaManager/App.xaml.cs`
- Modify: `StaminaManager/Application/AppCoordinator.cs`
- Test: `StaminaManager.Tests/ViewModels/SettingsViewModelTests.cs`

- [ ] **Step 1: 初期値と適用失敗の失敗テストを書く**

Assert first-run Windows theme resolution, Mica default, notifications ON/15 minutes, close-to-tray default, startup OFF, lead range `0..525600`, failed backdrop application retaining the last good setting, and fallback not overwriting the saved preference.

- [ ] **Step 2: 4カテゴリSettings UIを実装する**

Use native `ToggleSwitch`, `ComboBox`, `NumberBox`, `Button`, `InfoBar`. AutomationIds: `ThemeToggle`, `BackdropSelector`, `CloseBehaviorSelector`, `StartupToggle`, `NotificationsToggle`, `NotificationLeadInput`, `ExportBackupButton`, `ImportBackupButton`, `SettingsInfoBar`.

Add a notification-permission row whose status text is bound now and whose recovery button is wired in Task 11. Reserve AutomationId `OpenWindowsNotificationSettingsButton`; show it only when Windows notifications are unavailable.

- [ ] **Step 3: 5バックドロップを実装する**

Map Mica to `MicaBackdrop`, Acrylic to `DesktopAcrylicBackdrop`, Transparent to WinUIEx `TransparentTintBackdrop`, Blur to a `CompositionBrushBackdrop` implementation following WinUIEx's official `CreateHostBackdropBrush` example, and Solid to theme resources. High Contrast, transparency OFF, unsupported compositor, RDP, or apply exception must return a typed Solid fallback result.

Register Theme and Backdrop services in `App.xaml.cs`, add them to `AppCoordinator`, and inject only their Core-facing ports into `SettingsViewModel`. Verify one integration test changes the actual `MainWindow.SystemBackdrop`, not only the saved enum.

- [ ] **Step 4: テーマ・High Contrast・透明無効を起動確認する**

Use Light/Dark, all five backdrops, High Contrast, and Windows transparency OFF. Confirm content surfaces remain readable and saved preference returns after fallback conditions end.

- [ ] **Step 5: コミットする**

```powershell
git add StaminaManager.Core StaminaManager StaminaManager.Tests
git commit -m "feat: 外観とアプリ設定画面を実装"
```

### Task 10: ウィンドウ状態、単一インスタンス、トレイ、自動起動

**Files:**
- Create: `StaminaManager/Infrastructure/Windows/WindowStateService.cs`
- Create: `StaminaManager/Infrastructure/Windows/TrayService.cs`
- Create: `StaminaManager/Infrastructure/Windows/StartupService.cs`
- Create: `StaminaManager.Core/Abstractions/IWindowStateService.cs`
- Create: `StaminaManager.Core/Abstractions/ITrayService.cs`
- Create: `StaminaManager.Core/Abstractions/IStartupService.cs`
- Modify: `StaminaManager/MainWindow.xaml.cs`
- Modify: `StaminaManager/App.xaml.cs`
- Modify: `StaminaManager/Application/AppCoordinator.cs`
- Modify: `StaminaManager/Package.appxmanifest`
- Test: `StaminaManager.Tests/Windows/WindowBoundsPolicyTests.cs`

- [ ] **Step 1: 画面外補正ポリシーの失敗テストを書く**

Assert separate normal/compact persistence keys, disconnected monitor correction, DPI-adjusted bounds, normal minimum 520×520, compact minimum 360×480.

- [ ] **Step 2: WinUIEx WindowExとPersistenceIdを導入する**

Keep native caption buttons. Use distinct persistence IDs for normal and compact modes. Clamp restored bounds into the active work area before showing.

- [ ] **Step 3: close-to-trayと実終了を実装する**

WinUIEx `TrayIcon` exposes `開く` and `終了`; double-click restores/focuses. Window Closing cancels only for the configured tray behavior. Explicit tray exit sets an `isExplicitExit` guard so Closing is not canceled.

- [ ] **Step 4: StartupTaskと単一インスタンスを実装する**

Use packaged `StartupTask` only after user opt-in. If denied, restore toggle to actual state. Use Windows App SDK `AppInstance` redirection so second launch and notification activation focus the existing instance.

Register WindowState, Tray, and Startup services in `App.xaml.cs`; add the required ports to `AppCoordinator` and `SettingsViewModel`. During initial load, restore `LastDisplayMode` and `SelectedCompactGameId` from Task 8 before applying the corresponding normal/compact bounds. Add an integration check that a compact-mode exit and relaunch returns to compact mode with the same selected game.

- [ ] **Step 5: packaged integration checksを行う**

Launch twice and confirm one main instance; verify × to tray, tray open, double-click, explicit exit, close-as-exit, startup enable/disable/denied behavior.

- [ ] **Step 6: コミットする**

```powershell
git add StaminaManager.Core StaminaManager StaminaManager.Tests
git commit -m "feat: トレイとウィンドウ動作を統合"
```

### Task 11: 予約通知と端末固有通知台帳

**Files:**
- Create: `StaminaManager.Core/Models/NotificationLedgerEntry.cs`
- Create: `StaminaManager.Core/Models/NotificationState.cs`
- Create: `StaminaManager.Core/Abstractions/INotificationScheduler.cs`
- Create: `StaminaManager.Core/Abstractions/INotificationPermissionService.cs`
- Create: `StaminaManager.Core/Abstractions/ISettingsLauncher.cs`
- Create: `StaminaManager/Infrastructure/Notifications/NotificationLedgerStore.cs`
- Create: `StaminaManager/Infrastructure/Notifications/WindowsNotificationScheduler.cs`
- Create: `StaminaManager/Infrastructure/Notifications/NotificationPermissionService.cs`
- Create: `StaminaManager/Infrastructure/Windows/WindowsSettingsLauncher.cs`
- Modify: `StaminaManager/Application/GameManager.cs`
- Modify: `StaminaManager/Application/AppCoordinator.cs`
- Modify: `StaminaManager/App.xaml.cs`
- Modify: `StaminaManager/ViewModels/SettingsViewModel.cs`
- Modify: `StaminaManager/Views/SettingsPage.xaml`
- Modify: `StaminaManager/Package.appxmanifest`
- Test: `StaminaManager.Tests/Notifications/NotificationStateMachineTests.cs`

- [ ] **Step 1: 状態機械の失敗テストを書く**

Test deterministic key `GameId/fullAtTicks/lead`, future schedule, past-but-not-full immediate once, full/over-cap skip, `Scheduled→Consumed`, `Scheduled→re-register before due`, OFF `→Suppressed`, ON before due `→Scheduled`, ON after due `→Consumed`, name-only reschedule with same key, stamina/lead change with new key, restore retaining local consumed suppression, and checked subtraction when `fullAtUtc - leadMinutes` would be below `DateTimeOffset.MinValue`.

- [ ] **Step 2: 純粋な状態遷移を実装する**

Keep policy code in Core; Windows scheduling calls are an adapter. The ledger is `notification-state.json`, saved atomically, excluded from user backups, and pruned only for games/cycles outside the retained data horizon.

Implement notification-time calculation as a non-throwing result. A checked underflow returns `InvalidSchedule`, leaves the existing valid schedule untouched, and surfaces a validation `InfoBar`; malformed restored data is rejected before reconciliation.

- [ ] **Step 3: Windows App SDK予約通知を実装する**

Use game ID as Tag and `stamina` as Group. Cancel before replace. Persist `Scheduled` only after Windows accepts the schedule and `Consumed` only after immediate submission or after a scheduled entry disappears at/after its due time. Never retry a consumed key.

Register scheduler, ledger, and notification-permission services in `App.xaml.cs` and add them to `AppCoordinator`. `SettingsViewModel` must persist and then reconcile every notification ON/OFF or lead change: OFF cancels all and writes `Suppressed`; ON or lead change recalculates every game and reports partial failures without rolling back unrelated game data.

Expose current Windows notification availability and implement `OpenWindowsNotificationSettingsCommand` with the packaged Windows settings URI. Bind it to `OpenWindowsNotificationSettingsButton`; command failure returns a `SettingsInfoBar` error instead of being ignored.

- [ ] **Step 4: activation routingを実装する**

Toast arguments carry only game ID. Activation redirects to the primary instance, selects Overview, scrolls/focuses `GameCard_{Guid}`, or shows `OverviewInfoBar` when deleted.

- [ ] **Step 5: unitとMSIX integration testsを行う**

Run state-machine tests. Then schedule a notification 1–2 minutes ahead, close the app process, keep the PC awake and signed in, verify one notification arrives, click it, and confirm target focus. Repeat with notifications OFF, a lead change that causes rescheduling, and Windows notifications disabled; in the disabled case verify `OpenWindowsNotificationSettingsButton` launches the Windows notification settings page.

- [ ] **Step 6: コミットする**

```powershell
git add StaminaManager.Core StaminaManager StaminaManager.Tests
git commit -m "feat: 終了後も届くスタミナ通知を追加"
```

### Task 12: 安全なバックアップと全置き換え復元

**Files:**
- Create: `StaminaManager.Core/Validation/BackupLimits.cs`
- Create: `StaminaManager/Infrastructure/Backup/BackupManifest.cs`
- Create: `StaminaManager/Infrastructure/Backup/SafeZipReader.cs`
- Create: `StaminaManager/Infrastructure/Backup/BackupCoordinator.cs`
- Create: `StaminaManager.Core/Abstractions/IBackupService.cs`
- Create: `StaminaManager/Application/RestoreCoordinator.cs`
- Modify: `StaminaManager/ViewModels/SettingsViewModel.cs`
- Modify: `StaminaManager/Views/SettingsPage.xaml`
- Modify: `StaminaManager/App.xaml.cs`
- Modify: `StaminaManager/Application/AppCoordinator.cs`
- Test: `StaminaManager.Tests/Backup/SafeZipReaderTests.cs`
- Test: `StaminaManager.Tests/Backup/BackupCoordinatorTests.cs`

- [ ] **Step 1: 悪性ZIPと上限の失敗テストを書く**

Generate archives in memory for absolute paths, `..`, duplicate entries, path >200 chars, >202 entries, manifest >256 KiB, JSON >4 MiB, image >5 MiB, total >512 MiB, ratio >100, zero compressed/nonzero expanded, bad image, unknown required schema, and game count >100.

- [ ] **Step 2: 復元ジャーナルの失敗テストを書く**

Inject failure at `Validated`, `Staged`, `LocalCommitted`, and `Completed`. Assert old data remains before local commit, new data is authoritative after local commit, previous snapshot is recoverable, notification reconciliation resumes, StartupTask refusal writes OFF, and notification failure keeps data with retry state.

- [ ] **Step 3: SafeZipReaderとBackupCoordinatorを実装する**

Validate metadata before extraction, stream with byte counters, never call `ExtractToDirectory` on untrusted input, stage under `ApplicationData.LocalFolder`, and only rename within the same volume. Write `.staminabackup` containing manifest, schema JSON, and referenced assets; never include `notification-state.json` or absolute paths.

- [ ] **Step 4: preview/confirmation UIを実装する**

File pickers act only on user-selected files. Show game count, image count, theme, backdrop, notification, close behavior, and startup changes. Require explicit `現在データを置き換える`; cancel is default.

Register Backup and Restore services in `App.xaml.cs`, add the restore workflow to `AppCoordinator`, and bind export/import commands through `SettingsViewModel`. After `LocalCommitted`, call `AppCoordinator.ReconcileDerivedStateAsync` in the fixed order Theme/Backdrop → Window mode and selected game → StartupTask → notifications; surface each partial failure without undoing committed user data.

- [ ] **Step 5: テストを成功させてコミットする**

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj --filter "SafeZipReaderTests|BackupCoordinatorTests" -p:Platform=x64
git add StaminaManager.Core StaminaManager StaminaManager.Tests
git commit -m "feat: 検証付きバックアップと復元を追加"
```

### Task 13: アクセシビリティ、エラー状態、プライバシー仕上げ

**Files:**
- Modify: all `StaminaManager/**/*.xaml`
- Modify: `StaminaManager/Resources/Styles.xaml`
- Modify: `StaminaManager/Resources/Strings/ja-JP/Resources.resw`
- Modify: `StaminaManager/Package.appxmanifest`
- Create: `docs/privacy.md`
- Create: `docs/accessibility-checklist.md`

- [ ] **Step 1: 全操作要素へAutomationIdとAccessibleNameを付ける**

Verify keyboard tab order, Enter/Space card activation, visible focus, icon tooltips, dialog default/cancel buttons, and ring Value/RangeValue semantics. Remove fixed-height text containers that clip at 200% text.

- [ ] **Step 2: High Contrast・Reduced Motion・Reduced Transparencyを仕上げる**

Use system brushes in High Contrast, remove nonessential transitions in Reduced Motion, and force Solid while retaining saved backdrop preference in Reduced Transparency.

- [ ] **Step 3: エラー経路をUIへ接続する**

Cover corrupt data recovery, save retry, notification permission, StartupTask refusal, backdrop fallback, import rejection, and notification reconciliation failure using field errors or `InfoBar`; no swallowed exception and no user-sensitive log fields.

- [ ] **Step 4: manifestとコードのオフライン性を確認する**

Run:

```powershell
rg -n "HttpClient|WebRequest|Socket|Telemetry|Analytics|Crash|internetClient" StaminaManager StaminaManager.Core
```

Expected: no networking/telemetry implementation and no `internetClient` capability. Document that ApplicationData is removed on uninstall and backup is required.

- [ ] **Step 5: 全unit testsとRelease buildを実行してコミットする**

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj -c Release -p:Platform=x64
.\BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -SkipRun /p:Configuration=Release
git add StaminaManager StaminaManager.Core docs
git commit -m "fix: アクセシビリティとエラー表示を仕上げ"
```

### Task 14: UI Automationと視覚検証

**Files:**
- Create: `tests/ui/StaminaManager.UiTests.ps1`
- Create: `tests/ui/README.md`
- Create at runtime only: `tests/ui/results/*.json`
- Create at runtime only: `tests/ui/results/screenshots/<runId>/*.png`

- [x] **Step 1: 1パスのUI試験スクリプトを書く**

Script parameter is `[int]$AppPid`, never `$Pid`. Cover navigation, empty add card,
add/edit/delete-back/save, 3/2 columns and minimum-width 2-column retention, compact
switch/restore, persisted compact mode/selected game across relaunch, theme, five backdrops,
close behavior setting, notification toggle/lead rescheduling, disabled-notification settings
button, backup picker cancel, and every required AutomationId.

- [x] **Step 2: アクセシビリティ監査をスクリプトへ追加する**

Use `winapp ui inspect --interactive --json`; fail if app-owned Button/TextBox/NumberBox/ComboBox/ToggleSwitch lacks AutomationId or accessible name. Exclude system caption controls and picker hosts.

- [ ] **Step 3: 意味のある状態をスクリーンショット化する**

Capture empty Overview, 3-card wide, 2-column, minimum-width 2-column, editor validation,
compact, Settings Light/Dark, each backdrop, High Contrast, and 200% text. Save under ignored
`tests/ui/results/screenshots`.

通常表示は1行2～3件を正式仕様とし、1列確認は対象外とする。High Contrastと
200%テキストはOS全体の設定変更を伴うため、変更前状態を保存・復元する手動の
release gateとして実施する。

- [x] **Step 4: 起動してUIスイートを実行する**

```powershell
.\BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -Detach
.\tests\ui\StaminaManager.UiTests.ps1 -AppPid <launched PID>
```

Expected: script exits 0 and result JSON has zero failures.

- [x] **Step 5: 取得済みスクリーンショットを目視する**

Fail the task for clipping, overlap, unintended ellipsis/scrollbar, dead zones, unreadable transparent surfaces, missing focus, or inconsistent spacing. Fix and rerun at most two complete cycles.

- [ ] **Step 6: コミットする**

```powershell
git add tests/ui
git commit -m "test: 主要画面のUI自動試験を追加"
```

### Task 15: ブランド資産、MSIX、Store提出物

**Files:**
- Modify: `StaminaManager/Assets/*.png`
- Create: `StaminaManager/Assets/Brand/AppIconSource.svg`
- Modify: `StaminaManager/Package.appxmanifest`
- Modify: `StaminaManager/Properties/PublishProfiles/win-x64.pubxml`
- Create: `docs/release-checklist.md`
- Runtime only: `artifacts/StaminaManager.msix`
- Runtime only: `artifacts/*.msixupload`
- Runtime only: `artifacts/wack-report.xml`

- [ ] **Step 1: オリジナルのアプリアイコンとStore画像を作る**

Use the Instrument Cluster motif: one circular stamina arc, one clear center tick, Windows accent plus semantic red/orange/green only where legible. Generate all manifest-required scales; inspect light/dark taskbar and Store tile previews. Do not use game publisher marks.

- [ ] **Step 2: manifestをx64 Store方針へ固定する**

Keep a development identity locally. Remove x86/ARM64 build targets from solution/publish configuration without deleting generated profiles until the user confirms removal. Set display name, Japanese description, logos, app notifications, StartupTask, and no network capability.

- [ ] **Step 3: Release unit/UI testsを再実行する**

```powershell
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj -c Release -p:Platform=x64
.\BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -SkipRun /p:Configuration=Release
```

Expected: zero failed tests and successful x64 Release build.

- [ ] **Step 4: ローカル検証用の署名MSIXを生成する**

Generate a temporary development certificate outside Git tracking and package the Release layout:

```powershell
if ([string]::IsNullOrWhiteSpace($env:STAMINA_CERT_PASSWORD)) { throw 'STAMINA_CERT_PASSWORDをこのセッションへ設定してください。' }
$releaseLayouts = @(Get-ChildItem -LiteralPath 'StaminaManager/bin/x64/Release' -Directory -Recurse | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'AppxManifest.xml') })
if ($releaseLayouts.Count -ne 1) { throw "Release layoutが一意ではありません: $($releaseLayouts.Count)" }
winapp cert generate --manifest StaminaManager/Package.appxmanifest --output artifacts/devcert.pfx --password $env:STAMINA_CERT_PASSWORD --if-exists skip
winapp package $releaseLayouts[0].FullName --manifest StaminaManager/Package.appxmanifest --cert artifacts/devcert.pfx --cert-password $env:STAMINA_CERT_PASSWORD --output artifacts/StaminaManager.msix
```

Never commit or print the password/PFX. Install/trust only with explicit user approval because certificate trust changes machine state.

- [ ] **Step 5: Windows App Certification Kitを実行する**

This machine has `C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe`, but it requires an elevated process. Ask the user to run the following in an Administrator PowerShell; do not bypass elevation:

```powershell
$appCert = 'C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe'
Set-Location 'D:\Programming\CSharp\StaminaManager\.worktrees\initial-implementation'
& $appCert reset
& $appCert test -appxpackagepath 'artifacts/StaminaManager.msix' -reportoutputpath 'artifacts/wack-report.xml'
```

Expected: zero failed required tests. If WACK is removed before execution, report it as a release prerequisite instead of silently skipping.

- [ ] **Step 6: Partner Center関連付け後のStore uploadを生成する**

After the user associates the project with the reserved Store identity, build x64 StoreUpload with signing disabled for Store-side signing. Verify package identity and manifest version, then produce `.msixupload`. This step is blocked until Partner Center identity is supplied through Visual Studio's Store association; do not invent publisher values.

- [ ] **Step 7: 最終差分・全検証・コミットを行う**

```powershell
git status --short
git diff --check develop...HEAD
dotnet test StaminaManager.Tests/StaminaManager.Tests.csproj -c Release -p:Platform=x64
.\BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -SkipRun /p:Configuration=Release
git add StaminaManager docs/release-checklist.md ThirdPartyNotices.txt
git commit -m "build: Store提出用MSIX構成を追加"
```

Expected: worktree clean, tests/build/WACK pass, ignored artifacts remain untracked.

## 完了条件

- 設計仕様§15の全受け入れ条件をunit/integration/UI/WACKのいずれかで検証済み。
- x64 Release buildとローカル署名MSIXが生成済み。
- Microsoft Store用`.msixupload`はPartner Center identity関連付け後に生成済み、または外部依存として明示済み。
- `git diff --check develop...HEAD`にエラーがなく、秘密情報・証明書・生成物が追跡されていない。
- 実装完了時は`@superpowers:verification-before-completion`、統合判断時は`@superpowers:finishing-a-development-branch`を適用する。
