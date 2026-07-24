# StaminaManager 設計仕様

## 1. 目的

StaminaManager は、複数のソーシャルゲームのスタミナ回復状況をユーザーが
自由登録し、Windows 11 上で一覧確認・更新・通知できるデスクトップアプリで
ある。特定ゲームのAPIやプリセットへ依存せず、新作や地域版を含めてユーザー
自身が管理できることを重視する。

成功条件は次のとおり。

- 登録した全ゲームの現在スタミナ、状態、全回復までの時間を一画面で確認できる。
- 入力した基準値と時刻から、アプリ再起動後も現在値を正しく復元できる。
- アプリ終了後も、全回復前の通知がWindows通知センターに表示される。
- Microsoft Storeから安全にインストール・更新・アンインストールできる。
- Light/Dark、High Contrast、キーボード、Narrator、表示倍率へ対応する。

## 2. 確定した製品方針

| 項目 | 方針 |
|---|---|
| アプリ名 | StaminaManager |
| 作業場所 | `D:\Programming\CSharp\StaminaManager` |
| 対象OS | Windows 11 |
| CPU | x64のみ |
| 言語 | C# |
| ランタイム | .NET 10 |
| UI | WinUI 3 / Windows App SDK |
| 配布 | Microsoft Store向けMSIX |
| 更新 | Microsoft Storeによる自動更新 |
| アーキテクチャ | MVVM、Core分離、Windows機能はアダプター化 |
| 通信 | 完全オフライン |
| 計測 | 広告、アカウント、利用統計、クラッシュ送信なし |
| データ | Windowsのアプリ専用ローカル領域 |

Microsoft Partner Centerが割り当てるStore identityは公開工程でプロジェクトへ
関連付ける。開発とテストではローカル開発用のパッケージidentityを使用し、
アプリ機能はStore identityの具体値へ依存させない。

## 3. スコープ

### 3.1 初期リリースに含める

- 任意ゲームの追加、編集、削除。
- ゲーム名、現在スタミナ、最大スタミナ、1回復に必要な分数の入力。
- 任意のゲーム画像。未設定時は組み込み記号を表示。
- 3列を基準とするOverviewカードグリッド。
- 同一ウィンドウで切り替えるコンパクト表示。
- Light/Darkテーマ。
- Mica、Acrylic、Blur、Transparent、Solidバックドロップ。
- 通知ON/OFFと全回復何分前に通知するかの設定。
- Windowsログイン時起動のON/OFF。既定はOFF。
- 閉じるボタンの動作を「タスクトレイへ格納／終了」から選択。
- タスクトレイの「開く／終了」とダブルクリック復帰。
- アプリ終了後にも機能する予約通知。
- バックアップ書き出しと、確認付きの全置き換え復元。
- Microsoft Store提出用x64 MSIXビルド。

### 3.2 初期リリースに含めない

- ゲーム別プリセットや対応ゲーム一覧。
- ゲームアカウント、非公式API、公式APIとの連携。
- クラウド同期、複数PCの自動同期。
- 履歴グラフ、履歴ページ、独立したNotificationsページ。
- データ同期ボタン、Feedback、Helpのオンライン導線。
- 広告、課金、オンラインアカウント、テレメトリー。
- ARM64、Windows 10、ポータブル配布。
- 手動のカード並べ替え。初期リリースは登録順を保持する。

## 4. UXと画面構成

デザインの詳細トークンはリポジトリ直下の `DESIGN.md` を正とする。
UIモードは新規UIのため `MODE 1: CREATIVE MASTER` とし、美学は
**Precision Utility / Instrument Cluster** とする。差別化要素は、各カードの
円形スタミナ計器と、選択可能なWindowsバックドロップである。

### 4.1 Shell

- `NavigationView` の項目はOverviewとSettingsのみ。
- 標準幅では左ペイン、狭い幅ではコンパクトペインを使用する。
- カスタムタイトルバーはWindows標準のウィンドウ操作を壊さない。
- `WinUIEx.WindowEx` と `PersistenceId` を使用し、通常表示とコンパクト表示の
  位置・サイズを復元する。
- ウィンドウを再表示する際は既存インスタンスを前面へ移動し、多重起動を避ける。

### 4.2 Overview

- 標準幅は1行3カード、媒体幅は2カード、狭い幅は1カード。
- 登録順で表示する。
- 追加カードは最後のゲーム直後。空状態では最初の要素となる。
- ゲームカードには任意画像またはフォールバック記号、円形進捗、ゲーム名、
  `現在 / 最大`、状態ラベル、全回復までの時間を表示する。
- カード全体をクリックまたはEnter/Spaceで操作すると編集ダイアログを開く。
- 右上のコマンド領域に `コンパクト表示` を配置する。
- 読み込み中、空、保存失敗、通知無効の状態を専用表示する。

### 4.3 状態表示

表示用割合は `min(Current / Maximum × 100, 100)` とする。

| 割合 | 色 | ラベル |
|---|---|---|
| 0%以上50%未満 | 緑 | 余裕 |
| 50%以上80%以下 | オレンジ | 注意 |
| 80%超100%未満 | 赤 | 満タン間近 |
| 100% | 赤 | 満タン |
| 現在値が最大値を超過 | 赤 | 自然回復停止中 |

色、リング形状、数値、ラベルを併用し、色だけで状態を伝えない。

### 4.4 ゲーム追加・編集ダイアログ

入力項目は次のとおり。

- ゲーム名。必須。
- 現在スタミナ。0以上の整数。最大値超過を許可。
- 最大スタミナ。1以上の整数。
- 1回復に必要な時間。1分以上の整数。
- 任意のゲーム画像。PNG/JPEG、5MB以下、4096×4096以下。

追加時は `追加／キャンセル`、編集時は `保存／キャンセル／削除` を表示する。
削除ではゲーム名を示した確認ダイアログを開き、キャンセルを既定操作とする。
コンパクト表示の `現在値を更新` は同じダイアログを開き、現在値へ初期フォーカス
する。

### 4.5 コンパクト表示

- 同一ウィンドウを約420×520 effective pxのレイアウトへ切り替える。
- 上部で表示対象ゲームを選択する。
- 大きな円形進捗、現在値／最大値、状態、全回復までの時間を表示する。
- 操作は `現在値を更新`、`ゲームを編集`、`Overviewへ戻る` に限定する。
- `Overviewへ戻る` で切り替え前の通常ウィンドウ位置とサイズを復元する。
- 最後の表示モードと選択ゲームを保存し、次回起動時に復元する。

### 4.6 Settings

カテゴリはAppearance、General、Notifications、Dataの4つ。

#### Appearance

- Light/Darkテーマを `ToggleSwitch` で変更する。
- バックドロップを `Mica / Acrylic / Blur / Transparent / Solid` から選択する。
- 既定バックドロップはMica。
- 選択は即時プレビューし、正常に適用できた場合のみ保存する。
- High Contrast、Windows透明効果OFF、非対応GPU、Remote DesktopではSolidへ
  自動フォールバックする。保存済みの選択は失わない。
- Transparentでもカードと入力面は十分な不透明度を保つ。

#### General

- 閉じるボタンの動作。既定はタスクトレイへ格納。
- Windowsログイン時起動。既定はOFF。

#### Notifications

- 全回復前通知のON/OFF。
- 全回復の何分前に通知するか。0以上の整数で、0は全回復時刻を表す。
- 初期値は通知ON、全回復の15分前とする。
- Windows通知が無効な場合は状態と `Windowsの通知設定を開く` を表示する。

#### Data

- バックアップを書き出す。
- バックアップから現在データを置き換える。
- 復元前にゲーム件数、画像件数、設定概要を表示して確認する。

## 5. スタミナモデルと計算

### 5.1 GameEntry

`GameEntry` は少なくとも次を保持する。

- `Guid Id`
- `string Name`
- `int BaseStamina`
- `int MaxStamina`
- `int RecoveryMinutes`
- `DateTimeOffset RecordedAtUtc`
- `string? ImageAssetId`
- `int SortOrder`

現在時刻を `nowUtc` とする。`BaseStamina >= MaxStamina` の場合、自然回復は停止し、
現在値は `BaseStamina` のまま維持する。それ以外は次で求める。

```text
elapsedMinutes = max(0, nowUtc - RecordedAtUtc の総分数)
recovered = floor(elapsedMinutes / RecoveryMinutes)
current = min(MaxStamina, BaseStamina + recovered)
```

全回復時刻は次で求める。

```text
remaining = MaxStamina - BaseStamina
fullAtUtc = RecordedAtUtc + remaining × RecoveryMinutes
```

現在値を編集して保存した時刻を新しい `RecordedAtUtc` とする。ユーザーがゲーム内
の途中回復経過を入力する項目は設けない。画面表示中は定期的に再計算するが、算出
した現在値を毎回永続化しない。

計算はUTCで行い、表示時だけローカル時刻へ変換する。端末時計が過去へ変更された
場合も回復数を負にしない。

## 6. 通知

- パッケージidentityを持つMSIXとしてWindows通知を使用する。
- 通知内容はゲーム名と全回復までの情報を含む。
- ゲームIDを通知のTag/Groupへ割り当てる。
- ゲーム追加・編集・削除、通知設定変更、バックアップ復元時に対象通知を
  取り消して再予約する。
- 通知時刻は `fullAtUtc - leadMinutes`。
- 通知時刻が未来なら予約する。
- 通知時刻を過ぎているが全回復前なら、その場で一度通知する。
- すでに満タンまたは上限超過なら新規通知を予約しない。
- 通知OFFでは全予約を解除し、ONへ戻した際に全ゲームを再計算する。
- アプリ終了後も予約通知を維持する。
- 通知クリックでアプリを起動または前面化し、Overviewで対象カードへ移動する。
- PCの電源が通知時刻を長時間またいで切れていた場合、Windowsが通知を破棄する
  可能性を許容する。クラウドや常駐サービスによる保証配信は行わない。

## 7. ウィンドウ、タスクトレイ、自動起動

- `WinUIEx 2.9.2` の `WindowEx`、`WindowManager`、`TrayIcon` を使用する。
- 自前の `Shell_NotifyIcon` P/Invokeラッパーは作成しない。
- 閉じる動作がトレイ格納の場合、`Closing` を取り消してウィンドウを非表示にする。
- トレイメニューは `開く` と `終了`。
- ダブルクリックでもウィンドウを復帰する。
- `終了` はアプリプロセスを終了するが、予約済み通知は削除しない。
- 閉じる動作が終了の場合も同様に、予約済み通知を維持して終了する。
- ログイン時起動はパッケージ対応のStartupTaskで管理し、ユーザーがSettingsから
  有効化した場合だけ登録する。

## 8. バックドロップ

- MicaはWinUI 3標準 `MicaBackdrop`。
- AcrylicはWinUI 3標準 `DesktopAcrylicBackdrop`。
- BlurはWinUIEx `CompositionBrushBackdrop` を継承し、WinUIEx公式例に沿った
  `BlurredBackdrop` を実装する。
- TransparentはWinUIEx `TransparentTintBackdrop`。
- Solidはテーマ対応の単色背景。
- `BackdropService` が生成、適用、対応可否、フォールバックを一元管理する。
- 適用失敗は例外を握りつぶさず、Solidへ戻してローカル診断ログとInfoBarへ反映
  する。
- Reduced Transparency、High Contrast、透明効果OFFを優先し、視認性を損なう
  設定を強制しない。

## 9. データ保存とバックアップ

### 9.1 ローカル保存

- `ApplicationData.LocalFolder` のアプリ専用領域を使用する。
- `data.json` にスキーマバージョン、ゲーム、アプリ設定を保存する。
- ゲーム画像はランダム生成したファイル名で専用Assets領域へ保存し、JSONから
  `ImageAssetId` で参照する。
- JSONは一時ファイルへ書き込み、flush後に置き換える。
- 前回の正常スナップショットを保持する。
- 破損検出時は元ファイルを上書きせず、回復画面を表示する。
- アンインストール時にアプリ専用データが削除されることをSettingsに明記する。

### 9.2 バックアップ

- 拡張子は `.staminabackup`。
- ZIP内部にmanifest、スキーマ付きJSON、ゲーム画像を格納する。
- 書き出しと読み込みはユーザーが選んだファイルに対してのみ行う。
- 復元前に拡張子、manifest、スキーマ、総サイズ、エントリ数、パス、JSON、画像を
  すべて検証する。
- 絶対パス、親ディレクトリ参照、重複エントリ、過大展開を拒否する。
- 検証後にステージング領域へ展開し、全置き換えを行う。
- 失敗時は現在データを保持またはロールバックする。
- 復元確認にはゲーム件数、画像件数、テーマ、バックドロップ、通知、閉じる動作、
  自動起動の変更を表示する。

## 10. アーキテクチャ

ソリューションは次の3プロジェクトで構成する。

```text
StaminaManager.slnx
├─ StaminaManager
│  ├─ Views / ViewModels
│  ├─ Services
│  ├─ Infrastructure
│  ├─ Assets
│  └─ Package.appxmanifest
├─ StaminaManager.Core
│  ├─ Models
│  ├─ Calculations
│  ├─ Validation
│  └─ Abstractions
└─ StaminaManager.Tests
   ├─ Calculations
   ├─ Validation
   ├─ Persistence
   └─ Backup
```

依存方向は `Presentation → Application → Core` とし、InfrastructureはCoreの
インターフェースを実装する。CoreはWinUI、Windows App SDK、WinUIExへ依存しない。

主要な責務は次のとおり。

- `StaminaCalculator`: 現在値、割合、状態、全回復時刻を計算。
- `GameManager`: 追加、編集、削除、登録順を管理。
- `TimerCoordinator`: 表示中の再計算とViewModel更新を調整。
- `NotificationScheduler`: 予約、解除、クリック引数を管理。
- `LocalDataStore`: アトミック保存、読み込み、スキーマ移行。
- `AssetStore`: 画像検証、保存、参照、削除。
- `BackupCoordinator`: 検証、プレビュー、置き換え、ロールバック。
- `BackdropService`: 5種の外観とフォールバック。
- `TrayService`: WinUIEx TrayIconをアプリ操作へ接続。
- `StartupService`: StartupTaskの状態をSettingsと同期。

## 11. 依存関係

- 現行WinUIテンプレートが採用するMicrosoft Windows App SDK。
- `CommunityToolkit.Mvvm`。Observable stateとCommandのみに使用する。
- `WinUIEx 2.9.2`。WindowEx、位置サイズ復元、TrayIcon、カスタムBackdropに使用。
- `System.Text.Json`。データとmanifestのシリアライズに使用。

依存関係は必要最小限とし、バージョンをプロジェクトで固定する。第三者ライセンスを
Storeパッケージ内の通知ファイルへ記録する。

## 12. エラー処理とセキュリティ

- 入力エラーはフィールド直下に修正方法を表示し、保存を無効化する。
- 整数計算はchecked境界または事前検証でオーバーフローを防ぐ。
- 画像は拡張子だけでなくデコードして検証する。SVGは初期リリースで受け付けない。
- 書き込み失敗時は入力内容を画面上に保持し、原因と再試行方法を表示する。
- 通知権限が無効でもゲーム管理と計算は継続する。
- StartupTaskが拒否された場合はトグルを実状態へ戻して理由を表示する。
- バックアップはZip Slip、過大展開、破損、未知の必須スキーマを拒否する。
- ローカルログにはゲーム名、スタミナ値、画像パス、バックアップ内容を記録しない。
- 秘密情報、APIキー、認証情報は扱わない。
- ネットワークアクセスを要求する機能やcapabilityを追加しない。

## 13. アクセシビリティとローカライズ

- 初期UI言語は日本語。
- 文字列はreswリソースへ分離し、将来の言語追加を妨げない。
- すべての操作をキーボードで実行でき、論理的なフォーカス順を持つ。
- アイコンだけの操作にはAccessibleNameとToolTipを付与する。
- ProgressRing相当の表示には名前、現在値、最大値、状態をAutomationPropertiesで
  公開する。
- 色だけ、hoverだけ、音だけで重要状態を伝えない。
- Narrator、High Contrast、200%テキスト、Windows表示倍率へ対応する。
- Reduced MotionとReduced Transparencyを尊重する。

## 14. テスト戦略

### 14.1 単体テスト

- 0%、49%未満、50%、80%、80%超、100%、上限超過。
- 回復直前・直後、満タン到達、端末時計が過去へ変更された場合。
- UTC保存、ローカル表示、夏時間境界。
- 通知時刻の未来、経過済み、満タン済み、通知0分前。
- 入力検証、整数境界、画像制限。
- JSON round trip、破損JSON、スキーマ移行。
- バックアップの正常復元、未知スキーマ、Zip Slip、過大展開、ロールバック。

### 14.2 統合テスト

- MSIX identityで通知予約、再予約、解除、終了後表示、クリック起動。
- 通知OFF/ONとWindows通知無効状態。
- StartupTaskの有効化、無効化、拒否。
- WinUIEx TrayIconの開く、終了、ダブルクリック。
- Mica、Acrylic、Blur、Transparent、Solid切り替えと自動フォールバック。
- ApplicationData.LocalFolderへの保存とアンインストール時削除。

### 14.3 UIと手動検証

- 3列、2列、1列の境界幅。
- 空状態、追加、編集、削除、復元確認、保存失敗。
- 通常／コンパクト切り替えとウィンドウ位置・サイズ復元。
- Light/Dark、High Contrast、透明効果OFF、200%表示。
- キーボードのみ、Narrator、フォーカス表示、長い日本語ゲーム名。
- x64 Releaseビルド、MSIXインストール、更新、アンインストール。
- Windows App Certification KitとStore提出前検証。

## 15. 受け入れ条件

- 0件時に追加カードが先頭へ表示される。
- 標準幅で1行3ゲームカードを表示できる。
- 現在値の計算が再起動と時刻経過をまたいで一致する。
- 50%と80%の境界、および上限超過が仕様どおり表示される。
- カードとコンパクト表示の両方から現在値を更新できる。
- 5種のバックドロップを切り替えられ、非対応環境でSolidへ戻る。
- ×ボタンのトレイ格納／終了設定が動作する。
- 終了後にも予約通知が表示され、クリックで対象ゲームへ移動する。
- バックアップ復元が検証・確認・全置き換え・ロールバックを満たす。
- オフラインで全機能が動作し、ユーザーデータを外部送信しない。
- x64 MSIXがStore提出前検証を通過する。
