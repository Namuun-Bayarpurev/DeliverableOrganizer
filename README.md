# 納品物整理ツール（DeliverableOrganizer）

Azure DevOps の Pull Request（PR）の URL を入力するだけで、納品用の「コード（修正前後）」「仕様書」「テスト仕様書」をフォルダに自動で整理する Windows 向けデスクトップツール。

- 設計書：`納品物整理ツール UI・機能設計書.docx`（SharePoint、2026-11-24 版）
- 技術構成：C#（.NET 8）＋ WPF、MVVM（CommunityToolkit.Mvvm 8.4.0）
- 対象リポジトリ：Azure DevOps Repos（dev.azure.com、*.visualstudio.com）

## 現在の状態

**土台・画面と PR 情報の取得（C1）を実装済み（2026-11-25 時点）。** ファイルの出力はまだ行わない。
［確認］は Azure DevOps から PR 情報を取得して PR 一覧に表示する。［実行］は画面の動作確認用の仮の動きになっている。

## 起動方法

```powershell
dotnet run --project C:\MOON_WORK\01_Tools\DeliverableOrganizer
```

ビルドのみの場合は `dotnet build`。.NET 8 SDK が必要。Visual Studio では `DeliverableOrganizer.sln`（本体とテスト）を開く。

PR 情報の取得には、対象リポジトリの読み取り権限（Code: Read）を持つ PAT が要る。画面の PAT 欄に入れるか、環境変数 `AZDO_PAT` に設定しておく（画面の入力が優先）。
「保存する」をオンにすると、PAT を Windows 資格情報マネージャー（汎用資格情報 `DeliverableOrganizer:AzureDevOpsPat`）に保存し、次回起動時に PAT 欄へ自動で入る。保存はオンにした時点・［確認］［実行］時・終了時、オフにすると削除。

## exe の作成

```powershell
dotnet publish DeliverableOrganizer.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

`publish\DeliverableOrganizer.exe`（単一ファイル、約 0.4 MB）ができる。実行には .NET 8 デスクトップ ランタイムが要る。ランタイムなしで動かす場合は `--self-contained true` にする（サイズが大きくなる。どちらにするかは設計書 10 章の確認事項）。

## テスト

```powershell
dotnet test tests\DeliverableOrganizer.Tests
```

URL 解析と、偽の HTTP 応答を使った PR 情報取得（ページング、再試行、認証エラー）の単体テスト。Azure DevOps には接続しない。

## フォルダ構成

```
DeliverableOrganizer/
├─ App.xaml / App.xaml.cs        起動処理（MainWindow と MainViewModel の生成）
├─ Views/
│  ├─ MainWindow.xaml(.cs)       メイン画面（画面項目 ①〜⑯）
│  └─ PreviewWindow.xaml(.cs)    プレビュー画面（出力予定ツリー）
├─ ViewModels/
│  ├─ MainViewModel.cs           共通設定、実行／中止、進捗、ログ
│  ├─ CategoryTabViewModel.cs    タブごとの URL・パターン・出力範囲・PR 一覧
│  ├─ PrItemViewModel.cs         PR 一覧の 1 行
│  └─ PreviewViewModel.cs        プレビューのツリー（TreeNode）
├─ Models/
│  ├─ Enums.cs                   Category, ExistingFolderMode, TestScope, ChangeType, LogLevel
│  └─ PrModels.cs                PrRef, PrData, FileChange, CommitInfo, ExportOptions, LogEntry
├─ Services/
│  ├─ IDialogService.cs          ViewModel から画面操作するための窓口
│  ├─ DialogService.cs           フォルダ選択、プレビュー表示、エラー表示、エクスプローラー起動
│  ├─ PrUrlParser.cs             PR URL の解析（C1）
│  ├─ CredentialProvider.cs      PAT の決定（C2。画面 → 環境変数 AZDO_PAT）
│  ├─ PatStore.cs                PAT の保存・復元（Windows 資格情報マネージャー）
│  ├─ AzureDevOpsClient.cs       REST API 呼び出し（C1。ページング、再試行 3 回）
│  └─ PrLoader.cs                PR 情報を PrData にまとめる（C1。同じ PR は 1 回だけ取得）
└─ tests/
   └─ DeliverableOrganizer.Tests/  xUnit（本体の csproj は tests\ 配下を取り込まない設定にしている）
```

## 実装できたこと

### 土台
- [x] .NET 8 / WPF / CommunityToolkit.Mvvm のプロジェクト作成
- [x] 設計書 7 章のクラス構成に合わせた View / ViewModel / Model / Service の分割
- [x] 設計書のデータ構造を Model として定義（PrRef, PrData, FileChange, ExportOptions など。中身の処理は未実装）

### 画面（設計書 3・4 章）
| No | 項目 | 状態 |
|----|------|------|
| 1 | 出力先フォルダ＋［参照］ | 実装済み（フォルダ選択ダイアログ） |
| 2 | PAT（PasswordBox）＋「保存する」 | 実装済み（Windows 資格情報マネージャーに保存、起動時に復元） |
| 3 | ローカルリポジトリ＋［参照］ | 実装済み（未入力時はヒント表示） |
| 4 | 機能タブ | 実装済み。タブ名に登録件数を表示（空行・重複は数えない） |
| 5 | PR URL 入力（複数行） | 実装済み |
| 6 | ［確認］ | 実装済み（URL 解析と PR 情報取得。認証エラーはその場で中断してダイアログ表示） |
| 7 | PR 一覧 | 実装済み（PR 番号・タイトル・ブランチ・状態・変更ファイル数、エラー行は赤） |
| 8 | 対象パターン | 実装済み（仕様書・テストタブのみ表示） |
| 9 | 出力範囲 | 実装済み（テストタブのみ表示） |
| 10 | _PR情報.txt を出力 | 実装済み（既定 ON） |
| 11 | 既存フォルダ | 実装済み（上書き／削除して再作成／スキップ、既定は削除して再作成） |
| 12 | ［プレビュー］ | 実装済み。ツリーはカテゴリ／PR フォルダまで（変更ファイルは未表示） |
| 13 | ［実行］／［中止］ | 切替と中止は実装済み。出力処理は仮実装 |
| 14 | ［出力先を開く］ | 実装済み（完了後に有効、エクスプローラーで開く） |
| 15 | 進捗バー | 実装済み（処理済み件数／全体） |
| 16 | ログ | 画面表示は実装済み（警告はオレンジ、エラーは赤、自動スクロール）。ファイル出力は未実装 |

- ［実行］［プレビュー］は、出力先が入力されていて URL が 1 件以上あるときだけ有効
- 実行中は［確認］［プレビュー］［出力先を開く］を無効化

### 動作確認
- UI Automation で入力・タブ切替・確認・実行・中止・プレビューを操作し、期待どおり動くことを確認済み

## 今後実装するべきこと

推奨する順番で並べている。

### 1. PR 情報の取得（C1）
- [x] `PrUrlParser`：dev.azure.com／*.visualstudio.com（DefaultCollection 付き、プロジェクト省略形も可）の URL から組織・プロジェクト・リポジトリ・PR 番号を取り出す
- [ ] 形式が正しくない URL は実行対象から外す（今はタブの件数に含めている。［実行］の本実装と合わせて対応）
- [x] `AzureDevOpsClient`：REST API（api-version 7.1、Basic 認証）、HttpClient、再試行 3 回（通信エラー・5xx・429）
  - `pullrequests/{id}`、`iterations`、`iterations/{n}/changes?$compareTo=0`（2000 件ごとのページング、フォルダは除外）、`commits`（継続トークンでページング）
  - `items`（ファイル内容・全ファイル一覧）は C3 で追加する
- [x] `PrLoader`：PR・修正前 SHA（commonRefCommit）・修正後 SHA（sourceRefCommit）・変更一覧・コミット履歴（古い順）を `PrData` にまとめる
- [x] ［確認］を本実装に置き換え、PR 一覧にタイトル・ブランチ・状態・変更ファイル数を表示
- [x] 同じ PR は 1 回だけ取得する（`PrLoader` が 1 回の操作の中で結果を使い回す。［実行］でも 1 回の実行につき `PrLoader` を 1 つ作って使うこと）

### 2. 認証（C2）
- [x] `CredentialProvider`：画面の PAT → 環境変数 `AZDO_PAT` の順に使う
- [ ] `git credential fill` を 3 番目の手段として追加する
- [x] 認証エラー（401／403／203）で処理を中断し、「PAT の権限（Code: Read）を確認」ダイアログを出す（［確認］。［実行］は本実装時に `AzureDevOpsException.IsAuthError` で同様に止める）
- [x] PAT をログに出さない（例外メッセージ・ログに PAT を含めていない）

### 3. ファイル取得（C3）
- [ ] `IContentSource` / `GitContentSource`：`git cat-file blob {SHA}:{path}` で取得。コミットが無ければ `git fetch`
- [ ] `ApiContentSource`：`items?path=…&versionDescriptor.version={SHA}&$format=octetStream`
- [ ] git を優先し、失敗したら API で取得
- [ ] `SemaphoreSlim` で同時取得を 4 件までに制限

### 4. 出力（F1〜F3、C4、C6）
- [ ] `CodeExporter`（F1）：`コード\Pull Request {id}\before`／`after` に出力
  - 修正＝両方、追加＝after のみ、削除＝before のみ、名前変更＝旧パスで before／新パスで after
- [ ] `DocumentExporter`（F2・F3）：修正後のファイルを PR フォルダ直下に出力（削除ファイルは出力しない）
  - 仕様書 → `仕様書\`、テスト → `デバッグ\`
  - 対象パターン（カンマ区切り、例 `*.xlsx, */テスト仕様書/*`）で絞り込み
- [ ] `_PR情報.txt`（C4）：コミット履歴（古い順）と変更一覧
- [ ] 既存フォルダの扱い（C6）：上書き／削除して再作成／スキップ
- [ ] リポジトリ内のフォルダ構成を維持し、内容はそのまま書き込む（文字コード・改行コードは変換しない）
- [ ] 仮実装の `MainViewModel.RunAsync` を本実装に置き換える

### 5. ログ・設定（C7）
- [ ] `FileLogger`：画面と同じ内容を `<出力先>\_log\yyyyMMdd_HHmmss.log`（UTF-8）に出力
- [ ] `SettingsService`：`%APPDATA%\納品物整理ツール\settings.json` に保存し、次回起動時に復元（PAT は保存しない）
  - キー：OutputFolder, LocalRepository, SpecPatterns, TestPatterns, TestScope, WritePrInfo, ExistingFolder, PrFolderFormat, MaxParallel

### 6. エラー処理（設計書 9 章）
- [ ] 1 件の PR や 1 ファイルの失敗で全体を止めず、完了時にエラー件数を報告
- [ ] PR が存在しない（404）・通信エラー → その PR をスキップしてログにエラー
- [ ] ファイル取得失敗 → そのファイルだけスキップしてログに警告
- [ ] 出力先に書き込めない → 中断してエラーダイアログ
- [ ] ［中止］時は処理中のファイルを書き終えてから停止

### 7. その他
- [ ] C5 プレビュー：PR 情報取得後の変更ファイルまでツリーに表示し、実際のファイル数を出す
- [x] PAT を Windows 資格情報マネージャーに保存（「保存する」チェック。`Services/PatStore.cs`）
- [ ] 260 文字を超えるパスへの対応（`\\?\` 形式）
- [ ] PR フォルダ名の書式設定（既定 `Pull Request {id}`）
- [ ] 単体テスト（URL 解析と PR 情報取得は作成済み。パターン一致、出力パスの組み立てが残り）
- [ ] 配布設定（単一 exe）

### 確認事項が決まってから実装するもの（設計書 10 章）
- [ ] F3「PR 時点の全テスト仕様書」（Items API `recursionLevel=Full` または `git ls-tree`）
- [ ] コミット単位の before／after 出力（`Pull Request XXXX/コミット連番_SHA/before, after`）
- [ ] 対象パターンの既定値（仕様書・テスト仕様書の置き場所が決まってから）

## 確認事項が決まるまでの仮の動き

| 確認事項 | 仮の動き |
|----------|----------|
| F3 の出力範囲 | 既定は「PR の変更分」 |
| 既存フォルダがある場合の既定動作 | 「削除して再作成」 |
| 配布形態 | .NET 8 ランタイムが入っている前提で作成 |
