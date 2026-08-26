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
（＝事前に安く判定する方法が無い）ため、判定自体のゲーティングは行わず、OCR結果に既知タグが
一定数(`RecruitmentMonitorService.MinMatchedTagsForRecruitmentScreen`、現状3)未満しか含まれない
場合は以降の処理をスキップする、という形で結果的に求人画面以外では通知が発生しないようにしている。
公開求人のタグ選択画面は1枠あたり5〜6個のタグがランダムに表示される仕様である一方、それ以外の画面での
OCR誤検出はごく少数に留まるという実機での観察に基づく閾値。

ゲームの起動・終了検知は、`WindowCaptureService.FindWindowByProcessName`（プロセス/ウィンドウ列挙
のみでキャプチャを伴わない軽量な処理）を毎ティック呼ぶことで行う。ウィンドウのタイトルは表示言語に
よって変わる（例:日本語版では「アークナイツ」）ため、言語に関わらず変わらないプロセス名(`Arknights`)
の完全一致でウィンドウを探す。ウィンドウが見つかった時点で
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
    OperatorDataProvider.cs  オペレーター/タグデータの読み込み・利用可能ロケール一覧の取得
    AppSettingsStore.cs      言語設定の永続化(%LOCALAPPDATA%配下のJSON)
  Data/operators.ja-JP.json  オペレーター/タグデータ（日本版実データ。下記参照）
  Views/NotificationWindow.xaml(.cs)  画面隅の常時最前面通知ウィンドウ
tests/ArknightsRecruitRecommender.Tests/
  RecruitmentAnalyzerTests.cs  組み合わせ判定ロジックの単体テスト
tools/ArknightsDataGenerator/
  RecruitDetailParser.cs       gacha_table.jsonのrecruitDetailをレア度別名簿にパース
  OperatorDatasetBuilder.cs    名簿とcharacter_table.jsonを突き合わせてoperators.ja-JP.jsonを生成
  Program.cs                  CLIエントリポイント(データ取得元・出力先を引数で指定)
tools/ArknightsDataGenerator.Tests/  上記の単体テスト
```

## オペレーターデータの自動更新

`tools/ArknightsDataGenerator`は、[ArknightsAssets/ArknightsGamedata](https://github.com/ArknightsAssets/ArknightsGamedata)
から最新の`gacha_table.json`/`character_table.json`を取得し、`Data/operators.ja-JP.json`を
再生成するCLIツール。手動で行った抽出手順（recruitDetailのパース→character_table.jsonとの
突き合わせ→タグ構築）をそのままコード化したもので、生成結果は手動生成版と全156件で完全一致することを
確認済み。

想定外のデータ（未知のprofession値、recruitDetailとcharacter_table.jsonのレアリティ食い違い、
名前が解決できないケースなど）を検知した場合は、出力を書き込まずにエラー終了する
（`OperatorDatasetBuilder`が全件検証してから書き込むため、壊れたデータが気付かず生成されることを防ぐ）。

職業タイプ・位置のタグ名（「前衛タイプ」等）は言語ごとに異なる文字列だが、コードには決め打ちせず、
`gacha_table.json`の`gachaTags`から`tagId`をキーに動的に取得する。`tagId`の番号体系は
CN/JP/EN等リージョンをまたいで共通であることを確認済みなので、`--gacha-source`/`--character-source`
を他リージョンのURLに差し替えるだけで、そのリージョンの言語でタグが構築される
（2言語目以降のデータ追加時にコード変更が不要になるようにするための設計）。

```bash
dotnet run --project tools/ArknightsDataGenerator -- --output src/ArknightsRecruitRecommender/Data/operators.ja-JP.json
```

GitHub Actions (`.github/workflows/update-operator-data.yml`) が毎日9:00(JST)に自動実行し、
差分があればPRを作成する（直接コミットはしない。境界ケース、たとえば新規オペレーターが
`character_table.json`にまだ反映されていないタイミングでの実行等を人が確認できるようにするため）。
`workflow_dispatch`にも対応しているため、GitHub Actions画面から手動実行も可能。

## 言語設定

タスクトレイメニューの「言語」から、使用する言語（ロケール）を選択できる。選択肢は
`Data/operators.{ロケール}.json`という命名のファイルが`Data`フォルダに存在するかどうかから
動的に決まるため、新しい言語のデータファイルを追加するだけで選択肢に増える（コード変更不要）。
現在は日本版(`ja-JP`)とグローバル版(`en-US`)の2言語に対応している。

選択した言語は`OperatorDataProvider`（読み込むオペレーターデータ）と`TagOcrService`（OCR時に
要求する言語）の両方に連動する。ゲームの表示言語とOSの言語設定は必ずしも一致しないため、OSの
プロファイル言語からの自動選択（`OcrEngine.TryCreateFromUserProfileLanguages()`）には頼らず、
選択したロケールを常に明示的にOCRエンジンへ渡している。

選択時、その言語のOCR言語パックが端末にインストール済みかを`OcrEngine.AvailableRecognizerLanguages`
で事前に確認し、未インストールなら警告を出す（この判定はWindows側の言語パック登録がタグの主言語
部分だけ、例えば"ja-JP"ではなく"ja"、で行われることがあるため、完全一致ではなく主言語部分で比較する
必要がある）。設定は`%LOCALAPPDATA%\ArknightsRecruitRecommender\settings.json`に保存し、変更の
反映にはアプリの再起動が必要（実行中のインスタンスに対する常時監視のホットスワップは行わない）。

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

1. **`Data/operators.ja-JP.json` は日本版(Yostar)の実データ（156件）です。**
   `gacha_table.json`の`recruitDetail`フィールド（ゲーム内の「募集可能一覧」表示に使われている、
   レア度ごとに区切られたテキスト。これが公開求人プールの唯一の正確な一次情報源）から名簿を機械的に
   抽出し、`character_table.json`（職業・位置・特性タグ、`profession`/`position`/`tagList`）と
   `gachaTags`/`specialTagRarityTable`（★5=「エリート」、★6=「上級エリート」の特別タグ）を
   突き合わせて生成した。人材募集で入手可能なオペレーター全員ではなく、運営が求人プールに個別登録
   した閉じた名簿であるため、`character_table.json`の`itemObtainApproach`だけからは機械的に
   判定できない（この条件だけだと290件ヒットし、実際の3倍近くになる）。
   データソースは[Kengxxiao/ArknightsGameData_YoStar](https://github.com/Kengxxiao/ArknightsGameData_YoStar)
   ではない（2025年11月13日を最後に更新が止まっているため）。代わりに
   [ArknightsAssets/ArknightsGamedata](https://github.com/ArknightsAssets/ArknightsGamedata)
   （CN/EN/JP/KR/TW/biliの全リージョンを1リポジトリで自動取得・継続更新しているプロジェクト。
   2026年8月時点で日次〜週次更新が確認できる）の`jp/gamedata/excel/`配下を使用している。
   本国(CN)版データ（`cn/gamedata/excel/`）も同じリポジトリ内にあり同様に現役更新されているので、
   仮にJP側が将来止まった場合はCN版の`recruitDetail`とキャラクターIDで日本語版
   `character_table.json`を突き合わせる形を代替に検討すること。
   `Data/operators.en-US.json`（グローバル版、同じく156件）も同じツールを
   `--gacha-source`/`--character-source`をEN版のURLに差し替えて生成したもの。生成時に
   character_table.json側の名前が前後を単引用符で囲われている（例:`'Justice Knight'`。
   EN版で38件確認）ケースが見つかり、`recruitDetail`側は引用符無しのため突き合わせに失敗する
   問題があったので、`OperatorDatasetBuilder`で単引用符を正規化してから照合するよう対応済み。
   「ア」（6★特殊タイプ、遠距離、支援/火力タグ）は一文字だが実在するオペレーター名（データ誤りではない）。
   「カーディ」（★3、重装タイプ、近距離、防御）はCN/JP双方の`recruitDetail`（現在・過去とも）に
   一度も登場しておらず、公開求人プール対象外と判断して含めていない（一部サイトの掲載は誤りと判断）。
   [アークナイツ攻略Wiki](https://arknights.wikiru.jp/?%E5%85%AC%E9%96%8B%E6%B1%82%E4%BA%BA)
   （コミュニティによる継続更新、2026/07/28更新）の★6一覧33名と本データの★6一覧33名が完全一致する
   ことを確認済み。また同wikiに明記されている「公開求人対象外（中堅スカウト限定）」の6名
   （アンジェリーナ、エイヤフィヤトラ、スカイフレア、ソラ、フランカ、ラップランド）が本データに
   含まれていないことも確認済み。
   `OperatorDataProvider`は`locale`引数（既定`ja-JP`）で`Data/operators.{locale}.json`を読むため、
   将来的に他言語版（例: `en-US`）を追加する場合は同じ形式のファイルを追加するだけでよい。
2. **`Interop/GraphicsCaptureInterop.cs` はこのプロジェクトの中で最もバージョン依存が強い部分です。**
   .NET SDKやWindows SDKのアップデートでCsWinRTの相互運用の挙動が変わった場合、ここが壊れる可能性が
   最も高いので、動作しなくなったらまずここを疑うこと。
3. **実機（実際に起動中のアークナイツ）で一通り動作確認済み。** ウィンドウ検出は
   `WindowCaptureService.FindWindowByProcessName`（プロセス名`Arknights`の完全一致、表示言語に
   依存しない）で行う。日本語フォントはOCRで1文字ずつバラバラの単語として検出されることがあるため、
   `OcrWordClusterer`で近接した文字を結合してから`TagMatcher`のあいまい一致（既定で編集距離1まで
   許容）にかけている。`RecruitmentMonitorService.MinMatchedTagsForRecruitmentScreen`の閾値含め、
   実際のフォント描画・OCR精度を見ながら継続的な調整が必要な箇所ではある。
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
