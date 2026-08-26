# Arknights Recruit Recommender (Windows)

アークナイツ PC版の「公開求人」画面を常時監視し、星4以上が確定するタグの組み合わせを検出したら
画面隅に通知するタスクトレイ常駐アプリ。

## できること / できないこと

- 画面を読み取って通知するだけ。タグの自動選択・自動クリックなど、ゲームへの入力操作は行わない
  （多くのゲームの利用規約は自動入力・マクロ的操作を禁止しているため、意図的にスコープ外としている）。
- Windows標準の通知（トースト）は「集中モード」でゲームプレイ中に抑制されることがあるため使わず、
  自前の常時最前面ウィンドウで通知する。

## 技術構成

| 目的 | 採用技術 | 理由 |
|---|---|---|
| UI | C# / .NET 8 / WPF | 常駐トレイアプリ・Win32連携に対する実装コストが低い |
| 画面キャプチャ | `Windows.Graphics.Capture` (WGC) | ゲームはDirectX/GPU描画のため、`BitBlt`では黒画面になる可能性がある |
| 文字認識 | `Windows.Media.Ocr`（Windows標準OCR） | 外部依存（Tesseract等）を増やさず配布物を軽量に保てる |
| タグ検出 | OCR結果 + 既知タグ一覧のあいまい一致 | 解像度・アスペクト比違いによる座標ズレの影響を受けない |
| トレイアイコン | `H.NotifyIcon.Wpf` | WPF単体にはトレイアイコン機能が無いため |

### 解像度・アスペクト比への対応方針

ゲームクライアントは20種類以上の固定解像度（複数アスペクト比混在、レターボックス無し）を持つ。
固定ピクセル座標のテンプレートマッチングは解像度ごとに壊れるため、本アプリは
「ウィンドウ全体をキャプチャしてOCRでタグ文字列を探す」方式を採用し、解像度別の個別対応を不要にしている。

### 監視間隔とゲーム起動/終了検知

常時監視は1秒間隔でポーリングする。「公開求人画面かどうか」は結局OCRで画面を読まないと判定できない
（＝事前に安く判定する方法が無い）ため、判定自体のゲーティングは行わず、OCR結果に既知タグが1つも
含まれない場合は以降の処理をスキップする、という形で結果的に求人画面以外では通知が発生しないように
している。

ゲームの起動・終了検知は、`WindowCaptureService.FindWindowByTitle`（プロセス/ウィンドウ列挙のみで
キャプチャを伴わない軽量な処理）を毎ティック呼ぶことで行う。ウィンドウが見つかった時点で
`Windows.Graphics.Capture`のキャプチャセッションを開始し、以後は**セッションを使い回して**
最新フレームを取得するだけにすることで、1秒間隔でも毎回セッションを作り直すコストを回避している。
ウィンドウが見つからなくなった時点（＝ゲーム終了）でセッションを破棄し、GPUリソースを解放する。
ゲーム側で解像度設定が変更された場合は、フレームの`ContentSize`変化を検知してフレームプールを
作り直す。

`WindowCaptureService`はスレッドセーフではないため、`RecruitmentMonitorService`側で
`SemaphoreSlim`によるチェック処理の直列化を行い、常時監視のポーリングと手動チェック実行が
同時に走っても内部状態が競合しないようにしている。

## プロジェクト構成

```
src/ArknightsRecruitRecommender/
  App.xaml(.cs)              トレイアイコンの起動・監視サービスの配線
  Interop/                   Windows.Graphics.Capture を使うための低レベル相互運用コード
  Services/
    WindowCaptureService.cs  ゲームウィンドウのキャプチャセッション管理・フレーム取得
    TagOcrService.cs         OCRラッパー
    TagMatcher.cs            OCR結果と既知タグのあいまい一致
    RecruitmentAnalyzer.cs   タグ組み合わせ（1〜3個）ごとの確定レアリティ判定ロジック
    RecruitmentMonitorService.cs  上記を束ねるポーリングループ
    OperatorDataProvider.cs  オペレーター/タグデータの読み込み
  Data/operators.json        オペレーター/タグデータ（★要更新。下記参照）
  Views/NotificationWindow.xaml(.cs)  画面隅の常時最前面通知ウィンドウ
tests/ArknightsRecruitRecommender.Tests/
  RecruitmentAnalyzerTests.cs  組み合わせ判定ロジックの単体テスト
```

## 動作モード

常時ポーリング（1秒間隔でゲーム画面を監視し、★4以上確定の組み合わせを検出したら自動で通知）は
起動時から常に有効で、起動時引数による分岐は無い（IDEのデバッグ実行(F5)で起動しても、通常のexe
起動と挙動は完全に同じ）。これとは別に、タスクトレイ右クリックメニューの**「手動チェック実行」**は
起動方法によらず常時利用可能で、クリックすると手動で1回だけキャプチャ→OCR→タグ照合→組み合わせ判定を
実行し、結果を通知ウィンドウに表示する。常時監視の動作には影響しない。

**Debugビルドでのみ**、手動チェック実行時にキャプチャ画像(PNG)と判定結果(テキスト)を
`debug-output/` フォルダに書き出す（`#if DEBUG`で分岐。実機での動作確認・チューニング用）。
Release配布ビルド（GitHub Releasesで配る自己完結exe）では書き出しを行わず、一般ユーザーの
環境に余計なファイルを残さない。

## ⚠️ 既知の制約・要検証事項

1. **`Data/operators.json` はサンプルデータです。** 実際のゲームの全オペレーター・全タグを反映していません。
   実運用前に、[Kengxxiao/ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 等の
   公開データセットから `Name` / `Rarity` / `Tags` の形に変換して差し替える必要があります
   （フィールド名は本リポジトリの `Models/OperatorInfo.cs` に合わせる）。
2. **`Interop/GraphicsCaptureInterop.cs` はこのプロジェクトの中で最もバージョン依存が強い部分です。**
   .NET SDKやWindows SDKのアップデートでCsWinRTの相互運用の挙動が変わった場合、ここが壊れる可能性が
   最も高いので、動作しなくなったらまずここを疑うこと。
3. **実機（実際に起動中のアークナイツ）でのOCR精度は未検証。** `WindowCaptureService.FindWindowByTitle`
   に渡すウィンドウタイトルの文字列（現状 `"Arknights"` 決め打ち）や、`TagMatcher` のあいまい一致の
   閾値は、実際のウィンドウタイトル・フォント描画を見ながら調整が必要。
4. OCR言語パックがインストールされていないとOCRエンジンの生成に失敗する
   （設定 > 時刻と言語 > 言語と地域 から対象言語のOCR機能を追加）。

## ビルド・実行

```bash
dotnet build
dotnet run --project src/ArknightsRecruitRecommender
```

## テスト

```bash
dotnet test
```

## 配布用ビルド（自己完結・単一exe）

```bash
dotnet publish src/ArknightsRecruitRecommender -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

`v*` 形式のタグ（例: `v0.1.0`）をpushすると、GitHub Actions (`.github/workflows/release.yml`) が
上記と同じ内容でビルドし、zipにまとめてGitHub Releasesに自動添付する。
