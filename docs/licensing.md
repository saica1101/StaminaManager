# StaminaManagerのライセンス

最終確認日：2026年8月3日

> [!IMPORTANT]
> この文書はリポジトリとビルド成果物のライセンス監査記録であり、法律上の助言では
> ありません。配布条件に疑義がある場合は、各権利者または専門家へ確認してください。

## プロジェクト固有部分

StaminaManager固有のソースコードとプロジェクト固有の素材は、
`GPL-3.0-only`で公開します。ライセンス本文は[LICENSE](../LICENSE)を参照してください。

Windows App SDKなど、Microsoftが別ライセンスで提供するコンポーネントについては、
リポジトリの[Microsoft Components Exception](../LICENSE-EXCEPTION.md)に記載した
プロジェクト側の取り扱いを確認してください。この文書はMicrosoft製コンポーネントを
GPLへ再ライセンスするものではなく、個別コンポーネントの条件やStoreの要件に優先しません。

## 依存関係の監査結果

| 区分 | 主なコンポーネント | 適用条件 |
| --- | --- | --- |
| 実行時 | CommunityToolkit.Mvvm 8.4.2 | MIT |
| 実行時 | WinUIEx 2.9.0 | MIT |
| 実行時 | .NET Runtime 10.0.10 | MITおよび同梱の第三者通知 |
| 実行時 | Microsoft Edge WebView2 1.0.3719.77 | パッケージ同梱のライセンスおよび通知 |
| 実行時 | Microsoft Windows App SDK 2.3.1と推移依存 | Microsoft Software License Termsおよび同梱の第三者通知 |
| 実行時 | Microsoft Windows ML Runtime 2.1.74 | Microsoft Software License Termsおよび同梱の第三者通知 |
| 実行時 | System.Numerics.Tensors 9.0.0 | MITおよび同梱の第三者通知 |
| ビルド時 | Microsoft Windows SDK BuildTools | Microsoft Windows SDKのライセンス |
| ビルド／テスト時 | BuildTools.WinApp、MSTest、Microsoft.NET.Test.Sdkなど | MITまたは各NuGetパッケージの条件 |

依存コンポーネントを含む配布物についての最終的な条件は、各コンポーネントのライセンス、
通知、Storeの最新要件を確認してください。実際に確認できた通知と条件は
[ThirdPartyNotices.txt](../ThirdPartyNotices.txt)へ収録しています。

## Windows App SDK 2.3.1に関する確認事項

Microsoftの公式ダウンロードページではWindows App SDK 2.3.1がStable releaseとして
案内されています。一方、推移依存する`Microsoft.WindowsAppSDK.WinUI 2.3.0`のNuGet
パッケージ内`license.txt`には`MICROSOFT WINDOWS APP SDK ENGINEERING PREVIEW`と記載され、
live operating environmentでの利用を制限する文言があります。2.3.2でも同じ文面であることを
確認しています。

公式のStable表示と配布パッケージ内の条項が一致していないため、次のStoreパッケージを公開する
前にMicrosoftへ適用条件を確認するか、通常のWindows App SDKライセンスを同梱している
1.8系へ切り替えて再検証してください。GPLの追加許可は、Microsoft側の利用制限を緩和する
ものではありません。

## Microsoft Storeへ掲載するとき

Microsoft Storeの登録情報、追加ライセンス条項、依存コンポーネントの表示方法は、
提出時点のPartner Centerの案内と各権利者の文書を確認してください。プロジェクト側の
参照先として、次の公開URLを維持しています。

```text
https://github.com/saica1101/StaminaManager/blob/develop/LICENSE-EXCEPTION.md
```

このページはGPLv3本文への参照を含みます。既定ブランチを変更した場合はURLも更新してください。
Storeで配布するバージョンに対応するソースコード、ビルド手順、ライセンス本文、追加許可、
第三者通知を継続して公開できるよう管理します。

## 監査範囲

- `Directory.Packages.props`の直接依存関係
- `dotnet list package --include-transitive`で得られる推移依存関係
- 2026年8月2日に生成された`StaminaManager_1.0.0.0_x64.msixupload`内の実ファイル
- ローカルNuGetキャッシュに含まれる各パッケージのライセンス／通知文書

画像やアイコンについては、リポジトリ内に外部ゲームのロゴや第三者フォントが含まれていない
ことを確認しています。ユーザーがアプリへ登録するゲーム画像は、そのユーザー自身が利用権を
確認する必要があります。
