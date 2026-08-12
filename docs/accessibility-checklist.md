# アクセシビリティ チェックリスト

この文書は Stamina Manager の実装済み範囲と、リリース前に実機で確認する項目を
記録します。適合認証を示すものではありません。

## 実装済み

- NavigationView、ボタン、入力、選択、切り替えの各操作に一意な
  `AutomationId` と選択中の言語に対応した AccessibleName を設定する。
- ゲームカードはネイティブ `Button` として Enter / Space で開け、既定の
  可視フォーカスを維持する。
- スタミナ円形表示は現在値、最大値、割合、状態を読み上げ、読み取り専用の
  RangeValue パターンを公開する。
- アイコンだけの「Overviewへ戻る」ボタンに Tooltip と AccessibleName を
  設定する。
- 入力エラーは対象入力の直後に文言で表示し、処理エラーは再試行・設定確認・
  別ファイル選択などの次の操作を含む InfoBar で表示する。
- ContentDialog は既定ボタンとキャンセル／戻る操作を明示し、破壊的操作では
  キャンセル側を安全な既定にする。
- Light、Dark、High Contrast のテーマ辞書を持ち、High Contrast では
  `SystemColor*Brush` を使用する。状態は色だけでなく文言でも表示する。
- High Contrast、Windows の透明効果 OFF、リモートセッション、Backdrop
  非対応時は、保存済みの選択を変更せず Solid 背景へフォールバックする。
- 非本質的な独自アニメーションや画面遷移を使用しないため、Reduced Motion
  でも操作と情報は変化しない。
- テキストには固定 `Height` を指定せず、折り返し可能な TextBlock スタイルを
  使用する。

## 今回の機能に関する実装契約

- [x] Acrylic Slider は 0～100 の整数を 1 刻みで扱い、現在値をパーセントで
  読み上げる。実際に Acrylic が適用されている場合だけ有効で、Mica、Solid、
  Acrylic の Solid フォールバック中は無効になる。
- [x] `VersionFooterBand`／`VersionFooterText` は `PaneFooter` にあり、
  パッケージ由来のバージョンと選択言語に対応した AccessibleName を公開する。
- [x] `NavAbout`、`AboutPageRoot`、GitHub／README の各リンクボタンに一意な
  AutomationId と、既定のブラウザーで開くことが分かる Name／HelpText を設定する。
  About を開いただけではネットワーク通信を行わない。
- [x] `LanguageSelector` は日本語と English を選択でき、選択内容を保存して
  次回起動時に画面全体へ反映する。現在の画面を部分的に再翻訳しない。
- [ ] Acrylic の 0／50／100%、Mica／Solid／Solid フォールバック時の無効状態、
  VersionFooter、About のリンク、言語の再起動往復を実機 UIA で確認する。
  これは今回の文書同期では未実行であり、実機確認済みとは扱わない。

## Task 13 実機確認結果（2026-07-28）

- [x] x64 Release を 1120×760 で起動し、UIA の `get-focused` と
  `inspect` で確認したフォーカス対象はすべて `IsOffscreen=false` かつ
  画面内の境界を持っていた。追加カードでは既定の可視フォーカスも目視した。
- [x] 試験ゲームのカードをフォーカスし、`send-keys` の Enter と Space の
  それぞれで編集ダイアログを開き、Cancel をキーボードで実行できた。
- [x] 試験ゲームのカードは `Button` として一意な `AutomationId`、
  ゲーム名・現在値・最大値・状態・残り時間を含む Name を公開していた。
- [x] スタミナ円形表示は `ProgressBar` として一意な `AutomationId` と Name、
  読み取り専用 RangeValue（最小 0、最大 200、現在値 0）を公開していた。
- [x] 回復 InfoBar は静的契約テストと XAML／リソース確認により、一意な
  `AutomationId`、日本語 Name、`LiveSetting=Polite`、閉じる操作、
  回復後の次アクションを持つことを確認した。破損データの注入はしていない。
- [x] 横長カードを 1120×760 の Dark 表示で目視し、カードとリングの
  クリップや重なり、行内の不自然な空白、意図しないスクロールバーがないことを
  確認した。長いゲーム名は指定済みの省略表示になり、完全な Name は UIA で
  取得できた。
- [x] `TabFocusNavigation="Local"` 適用後の Tab／Shift+Tab を確認し、前進で
  ゲームカードから追加カード、逆進で追加カードからゲームカードへ視覚順に
  移動できた。どちらも `IsOffscreen=false` で画面内の境界を持ち、右／左矢印でも
  両カードの間を双方向に移動できた。
- [x] 試験ゲームはキーボードで削除し、アプリ停止後に起動前のデータを復元して
  全ファイルの SHA-256 一致を確認した。

## Task 14 で扱う UIA／視覚確認

- [x] Overview の ItemsRepeater に `TabFocusNavigation="Local"` を設定し、
  専用の静的契約テストと Release UIA 実機確認で、Tab／Shift+Tab が
  ゲームカードと追加カードを左から右／右から左の順に辿ることを確認した。
- [x] Overview、カード、追加、Settings、各入力、バックアップ、ダイアログの
  保存／戻る／キャンセルを含む 1 パスの UIA スクリプトを実行する。
- [x] 3列／2列の境界幅、Standard最小幅2列、Compact、Settings、入力エラー、
  Light／Dark、Mica／Acrylic／Solidの3種類のBackdropのスクリーンショットを取得し、
  クリップ、重なり、
  省略、スクロールバー、空白、フォーカス、余白を目視する。
- [x] Windows の「水生」Contrastテーマと200%テキストでUIスイートを実行し、
  3列／2列／Standard最小幅2列、Compact、Settings、編集ダイアログの
  スクリーンショットを取得した。200%は16件成功・失敗0件、Contrastテーマは
  主要15件成功で、透明背景をSolidへ強制する既定動作も目視した。
- [x] 表示したアプリ所有の操作要素について AutomationId、Name、role を
  スクリプトで監査し、主要な value／state は各操作シナリオで照合する。
- [x] Windows全体の通知を一時的に無効化し、状態依存の
  `OpenWindowsNotificationSettingsButton`、案内文、通知設定ページへの遷移を
  UIAで確認した。UIスイートは17件成功・失敗0件で、アプリデータと設定の
  復元fingerprintも一致した。

## リリース前の手動 release gate（未実施）

- [ ] Narrator で各操作の名前、切り替え状態、入力値、エラー、スタミナの
  現在値・最大値・割合・状態を確認する。
- [x] Windows のテキスト サイズ 200% で見出し、本文、入力エラー、InfoBar、
  ダイアログ操作が切れず、スクロールで到達できることを確認する。
- [ ] Light、Dark、および Windows の各 Contrast テーマで、境界、フォーカス、
  文字、状態文言が判別できることを確認する。
- [ ] 「アニメーション効果」をオフにしても、操作が遅延せず情報が失われない
  ことを確認する。
- [ ] 「透明効果」をオフにした場合とリモートセッションで Solid 背景になり、
  Settings の保存済み Backdrop 選択が維持されることを確認する。
- [ ] 破損データ、保存失敗、通知拒否、StartupTask 拒否、Backdrop フォールバック、
  不正バックアップ、通知再調整失敗の各案内に次の操作が表示されることを確認する。
- [ ] Acrylic Slider、VersionFooter、About の GitHub／README リンク、
  日本語／English の次回起動反映を、Light／Dark／High Contrast と狭幅で確認する。
  文書同期時点では実機 UI 確認を行っていない。
- [ ] 100%、125%、150%、200% の表示スケールと最小／標準ウィンドウ幅で、
  コントロールが重ならず、フォーカス対象が隠れないことを確認する。
