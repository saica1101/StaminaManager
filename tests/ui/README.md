# Stamina Manager UI テスト

`StaminaManager.UiTests.ps1` は、起動済みの Stamina Manager を起点に、
`winapp ui` で操作する1パスのUIテストです。空データの決定的な fixture、
3ゲームのレイアウト、再起動を伴うコンパクト表示の永続化、Settings、
通知 ledger、バックアップ picker、UI Automation を確認します。

## 実行前提

- 対話可能でロックされていない Windows デスクトップで実行する。
- `winapp` を PATH から実行できるようにする。
- 対象アプリを開発用（Store署名ではない）package として登録し、起動しておく。
- 実行中は対象アプリを操作しない。

```powershell
.\BuildAndRun.ps1 StaminaManager/StaminaManager.csproj -Detach
.\tests\ui\StaminaManager.UiTests.ps1 -AppPid <launched PID>
```

`AppPid` は必須です。PowerShell の読み取り専用自動変数 `$Pid` と衝突しない
名前を使用しています。

Store版（`SignatureKind=Store` または `WindowsApps` 配下）のPIDは安全ガードで
拒否します。Store版のプロセスやLocalStateをUIテストへ指定しないでください。
このガードはプロセス停止やLocalState退避より前に評価され、対象外PIDはテストを
開始せずエラー終了します。Store版を検証する結果として扱わず、開発用packageを
指定し直してください。

## 安全性

スクリプトは、PID のプロセス名、`StaminaManager.exe` の絶対パス、登録済み
Appx package の `InstallLocation` を照合してから操作します。照合に失敗した
PID は停止しません。

package family ごとの named mutex を確保し、同じ package に対するUIテストの
多重実行を拒否します。実行IDはミリ秒とランダム値を含むため、結果や画像も
別の実行と衝突しません。

試験開始時は同じ PID かつ同じ実行パスのプロセスだけを先に停止します。その後、
package の `LocalState\Data` と `Settings` を一時ディレクトリへコピーし、
ファイルパス、サイズ、SHA-256 を含む fingerprint を照合します。これにより、
アプリの停止と退避の間に書き込みが抜け落ちることを防ぎます。退避後は元の
settings を保持した `games=[]` fixture と空の通知 ledger を書き、実行ファイルの
`AppX` 親を `winapp run` へ渡して再起動します。

`finally` では現在の PID と実行パスを再検証して停止します。元データを一度
読み込ませて Windows 通知を再調停し、そのプロセスも同じ条件で停止した後、
元の `Data` と `Settings` を戻して両方の fingerprint を再照合します。
元ファイルは退避先から戻すため、成功後の一時ディレクトリにユーザーデータの
複製は残りません。プロセス名だけを使った一括停止や、ユーザーデータの削除は
行いません。

テストまたは再調停で元データに存在しなかったファイルだけが生じた場合は、
結果 JSON の `generatedResidueDirectory` に記録します。復元失敗時は終了コードが
`1` になり、未使用のバックアップが残っていれば `dataBackupDirectory` と
`settingsBackupDirectory` から確認できます。

## 自動確認する範囲

- Overview と Settings の往復
- 空の Overview と、空のゲーム名で保存できないこと
- 3ゲームの追加、編集、削除確認から戻る、保存
- content 720 effective pxで3列、719 effective pxとStandard最小幅で2列となる
  カード bounds
  （UI Automation の物理pxをウィンドウDPIで換算）
- コンパクト表示、選択ゲームと bounds の再起動永続化、通常 bounds の復帰
- Light / Dark と Mica / Acrylic / Solid
- Acrylic 0% / 50% / 100% の適用、永続化、再起動後の復元
- 左ペインの `VersionFooterBand`／`VersionFooterText` と About の表示
- About の GitHub／README リンクの固定URL、既定ブラウザー起動契約
- English選択→再起動→英語表示、日本語選択→再起動→日本語表示
- 言語テスト終了時の開始言語への復元と、復元失敗時の結果記録
- バックアッププレビューの opacity／language と、復元後の次回起動反映
- 閉じる動作の selector と、WM_CLOSE による tray 格納・redirect 起動復帰
- 通知 wrapper の `NotificationLeadInput`、内部 `InputBox` の操作、通知 ledger の
  `Suppressed` → `Scheduled`、通知リード変更による再予約
- export / import backup picker のキャンセル
- 表示した app-owned の Button、TextBox / Edit、NumberBox / Spinner、
  ComboBox、ToggleSwitch に AutomationId と accessible name があること
- 必須 AutomationId が収集した状態のいずれかへ現れること

## テーマ・背景切り替えのクラッシュ回帰テスト

`appearance-navigation-stress.ps1` は、Settings で Light / Dark と
Mica / Acrylic / Solid を切り替えながら、
`Settings → Overview → Settings` を最低3周往復します。各操作の直後だけでなく
待機中も同じ PID と実行ファイルパスの生存をポーリングするため、背景変更後に遅れて
発生するクラッシュも検出します。実際に適用された背景は、UI Automation の
`ActualBackdropDiagnostic` で確認します。

Acrylic を選択しても環境制約により `Solid|SolidSurface=Visible` になった場合、
Acrylic の0／50／100%操作とAcrylic専用の永続化確認は理由付き `SKIP` になります。
これはAcrylicの適用成功を意味しません。Mica／Solidの選択とSlider無効状態、
Solid fallbackの診断は引き続き確認対象です。Acrylic選択後にSolidでもAcrylicでもない
診断値になった場合は `FAIL` として扱います。

起動済みアプリの PID を指定して実行します。同じ package の別UIテストとは named
mutex で競合を防ぎます。

```powershell
.\BuildAndRun.ps1 .\StaminaManager\StaminaManager.csproj -Detach
.\tests\ui\appearance-navigation-stress.ps1 -AppPid <launched PID>
```

スクリプトは初期テーマと背景を保存し、成功・失敗にかかわらず `finally` で復元を
試みます。プロセスが終了して復元できない場合は、結果JSONに復元不能と初期値、
package、実行ファイル、`data.json` の各パスを記録します。次回起動後はJSONの
`restoration.initialThemeName` と `initialBackdrop` を Settings から設定し直して
ください。ユーザーデータファイルを直接編集・削除する処理はありません。

スクリプトは1つのPIDを1回だけ試験します。1回で再現しない場合は、
操作者が新しいプロセスを起動し、合計最大3回まで繰り返します。
3回とも生存した場合はPASSを「今回の環境では非再現」として記録し、
クラッシュを再現したことにはしません。
結果JSONの `status` は次のように解釈します。

- `PASS_NON_REPRODUCED`: 3周、初期値の復元、復元後15秒間の生存確認が
  完了。クラッシュの修正済みを意味するのではなく、その実行では非再現。
- `RED_CRASH_REPRODUCED`: 同じPIDのプロセス消失または実行ファイルパス変化を
  検出。`restoration` で復元成否と次回起動時の操作を確認。
- `FAIL`: UI Automation操作、診断値、復元のいずれかが失敗したが、
  プロセスクラッシュは検出していない。

`SettingsInfoBar`、`OpenWindowsNotificationSettingsButton`、各エラー InfoBar、
復元確認ボタンなどは状態依存です。スクリプトは XAML source に宣言があることを
確認し、実行中に非表示なら ID ごとの具体的理由とともに `SKIP` を記録します。

言語テストは English へ変更して再起動し、主要画面のリソースと AutomationProperties を
確認した後、日本語へ戻して再起動します。開始時の言語へ戻せない場合は、UIテスト自体が
成功していても `languageRestorePassed=false` として終了コード1になります。

## 出力

- `tests/ui/results/ui-results-<timestamp>.json`
- `tests/ui/results/screenshots/<runId>/*.png`

JSON は `PASS`、`FAIL`、`SKIP` の各結果と、データ復元結果を含みます。
スクリーンショットは UIA では検出できないクリッピング、重なり、意図しない
省略記号やスクロールバー、読みにくい透明面、余白の不整合を目視確認します。

Windows Graphics Capture は、テーマ・背景の切り替え直後に前フレームを返すことが
あります。スクリプトは各 capture 前に500ms待機し、Light / Dark と Acrylic は
`--capture-screen` を使います。それでも環境によって不安定な場合は、
結果 JSON と状態を確認して完全な1パスを再実行し、失敗画像だけを合成しないで
ください。

## OS状態を変更して確認する項目

次の項目は OS 全体の状態変更を伴うため、通常状態の実行では JSON に `SKIP` を
記録します。

- High Contrast
- 200% テキストスケール
- Windows 通知無効状態

Windows通知が実際に無効な状態で実行した場合、スクリプトは
`OpenWindowsNotificationSettingsButton` の表示、AutomationIdとName、
Windows通知設定ページへの遷移を自動確認します。この実行では通知予約を生成せず、
通知ledgerの予約試験だけを理由付きで `SKIP` にします。通知が有効な通常実行では、
従来どおり通知ledgerの抑止、予約、リード時間変更による再予約を確認します。

これらを確認するときは、元の OS 設定を記録してから変更し、各状態の画像を
追加で保存した後、必ず元へ戻してください。
