# ConsecutiveSnap 技術設計書 (v0.1 ドラフト)

要件定義（[requirements.md](./requirements.md)）を実現するための技術方針。
同作者のスクリーンショット注釈アプリ SnipSnap の実装を参考にし、必要な部分だけをコピーして使う。SnipSnap 側は変更しない。

## 1. 技術スタック

| 項目 | 選定 | 理由 |
|---|---|---|
| ランタイム | .NET 8 (LTS) | SnipSnap と同一。自己完結型 exe を作れる |
| UI | WPF | 範囲選択オーバーレイを SnipSnap から流用できる |
| 画面キャプチャ | `Graphics.CopyFromScreen` | SnipSnap と同一。追加依存なし |
| 入力送信 | `SendInput`（user32.dll, P/Invoke） | キー・マウスとも OS 標準 API で実現できる |
| 緊急停止 | `RegisterHotKey`（`Pause/Break`, 修飾キーなし） | SnipSnap の `GlobalHotkeyService` をそのまま流用できる |
| 設定保存 | System.Text.Json（.NET 標準） | 外部依存を増やさない。ステップ列はポリモーフィック対応 |
| MVVM ライブラリ | **使わない** | CommunityToolkit.Mvvm 等の NuGet を製品に入れない（サプライチェーン攻撃面の最小化）。画面は少ないため、code-behind と最小限の `INotifyPropertyChanged` 実装で足りる |
| テスト | xUnit（**テストプロジェクトのみ**） | 製品の exe には含まれない。追加時はリリース直後の版を避け、実績のある安定版を固定指定（`^` やワイルドカード不可） |

### 外部API・ネットワークについて
- 外部 Web API・ネットワーク通信は**一切行わない**
- 使うのは Win32 API（user32 / gdi32）のみ: `SendInput`, `SetCursorPos`, `GetCursorPos`, `RegisterHotKey`, `UnregisterHotKey`, `SetWindowPos`, `SetWindowDisplayAffinity`, `GetSystemMetrics`

## 2. プロジェクト構成

```
ConsecutiveSnap.sln
├─ src/
│  ├─ ConsecutiveSnap.Core/     # UI 非依存のロジック（テスト対象）
│  │   ├─ Steps/                #   ActionStep（Key/Click/Wait）
│  │   ├─ Settings/             #   AppSettings, SettingsStore
│  │   ├─ Storage/              #   FolderAllocator, FileNameFormatter, FolderNameValidator
│  │   ├─ Capture/              #   IScreenCapturer, ScreenCapturer（SnipSnap から改造）
│  │   ├─ Input/                #   IInputSender, Win32InputSender
│  │   └─ Runner/               #   CaptureRunner（実行エンジン）, RunOptions, RunResult
│  └─ ConsecutiveSnap.App/      # WPF 本体
│      ├─ MainWindow            #   設定入力・開始/停止・結果表示
│      ├─ RegionPickerWindow    #   範囲/座標指定オーバーレイ（SnipSnap から改造）
│      ├─ PickSession           #   モニター間で共有する選択状態（SnipSnap の CaptureSession を改造）
│      ├─ ProgressWindow        #   実行中の小さな進捗表示（撮影に写らない）
│      ├─ StepEditorDialog      #   ステップの追加・編集
│      ├─ EmergencyStopHotkey   #   Pause/Break 登録（GlobalHotkeyService を流用）
│      └─ app.manifest          #   PerMonitorV2（SnipSnap と同一）
└─ tests/
   └─ ConsecutiveSnap.Core.Tests/
```

- Core は `net8.0-windows`（System.Drawing を使うため）。App は `net8.0-windows` + WPF + WinForms 参照（`NativeWindow` と `Screen.AllScreens` のため。SnipSnap と同様に `System.Windows.Forms` / `System.Drawing` の暗黙 using は外し、WPF の型名衝突を避ける）
- **SnipSnap にある常駐プロセス・トレイ・注釈編集は不要**。通常の WPF アプリ（メインウィンドウ1枚）にする

## 3. SnipSnap からの流用方針

| SnipSnap のファイル | 扱い | 変更点 |
|---|---|---|
| `Core/Capture/ScreenCaptureService.cs` | コピーして改造 | 仮想スクリーン全体に加え、**指定矩形のみ撮影**する `CaptureRegion(Rectangle)` を追加 |
| `App/CaptureOverlayWindow.xaml(.cs)` | コピーして改造 → `RegionPickerWindow` | 「矩形選択」に加え「1点クリック選択」モードを追加。Enter 確定は不要（SnipSnap と同様、マウスを離すと確定） |
| `App/CaptureSession.cs` | コピーして改造 → `PickSession` | 点モードの結果を保持するフィールドを追加 |
| `App/App.xaml.cs` の `CreateCaptureSession` / `RunCaptureOverlay` | 考え方をコピー | モニターごとにオーバーレイを作り、`DispatcherFrame` で全て閉じるまで待つ |
| `App/GlobalHotkeyService.cs` | コピー | 修飾キーなし（`ModNoRepeat` のみ）で `VK_PAUSE (0x13)` を登録。ホットキー変更 API（`TryUpdateHotkey`）は不要なので削除 |
| `App/app.manifest` | コピー | アセンブリ名のみ変更 |
| `Core/Storage/ScreenshotStorageService.cs` | **流用しない** | 採番規則が異なる（本アプリはフォルダ内を 1〜N で固定採番） |
| 注釈・コマンド・履歴・トレイ・常駐 | **流用しない** | 不要 |

### 座標系の方針
- キャプチャ範囲・クリック座標は、保存時も実行時も**物理スクリーン座標（仮想スクリーン原点基準、Win32 デスクトップ座標）**で持つ。ビットマップ座標への変換はオーバーレイ内（`origin` を足し引きするだけ）に閉じ込める
- アプリは PerMonitorV2 を宣言するため、`CopyFromScreen` / `SetCursorPos` / `SendInput` / `GetSystemMetrics` はすべて物理ピクセルで統一される

## 4. データモデル

```csharp
// Core / UI 非依存
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyStep),   "key")]
[JsonDerivedType(typeof(ClickStep), "click")]
[JsonDerivedType(typeof(WaitStep),  "wait")]
public abstract record ActionStep;

public sealed record KeyStep(int VirtualKey, KeyModifiers Modifiers) : ActionStep; // Ctrl/Alt/Shift/Win のフラグ
public sealed record ClickStep(int X, int Y) : ActionStep;                          // 物理スクリーン座標
public sealed record WaitStep(int Milliseconds) : ActionStep;

public sealed class AppSettings
{
    public RectDto? CaptureRegion { get; set; }            // 物理スクリーン座標（未指定なら null）
    public List<ActionStep> Steps { get; set; } = new();
    public int RepeatCount { get; set; } = 10;             // 1〜9999
    public int StartDelaySeconds { get; set; } = 5;        // 0〜600
    public int AfterSequenceWaitMs { get; set; } = 500;    // 0〜60000
    public bool RestoreCursorAfterClick { get; set; } = true;
    public string ParentFolder { get; set; } = "<Pictures>\\ConsecutiveSnap";
    public string FolderName { get; set; } = "";
}
```

- `Steps` は空でもよい（撮影のみを回数分繰り返す）
- 設定は `%APPDATA%\ConsecutiveSnap\settings.json`。読み込み後に**範囲チェックと補正**（回数・待機時間のクランプ、`CaptureRegion` が現在の仮想スクリーン外なら null に戻して再指定を促す）を必ず行う

## 5. 実行エンジン（CaptureRunner）

UI から切り離し、Core 内で完結させる。入出力はインターフェース経由にして、実機なしでテストできるようにする。

```csharp
public interface IScreenCapturer { Bitmap Capture(Rectangle screenRect); }
public interface IInputSender
{
    void SendKey(int virtualKey, KeyModifiers modifiers);
    void ClickAt(int x, int y, bool restoreCursor);
}
public interface IDelay { Task Wait(TimeSpan time, CancellationToken ct); }  // テスト時は即時完了版に差し替える

public sealed class CaptureRunner
{
    public Task<RunResult> RunAsync(RunOptions options, IProgress<RunProgress> progress, CancellationToken ct);
}
```

### 実行手順（`RunAsync`）
1. `StartDelaySeconds` を 1 秒刻みでカウントダウン（`progress` に残り秒を通知）
2. `i = 1 .. RepeatCount` を繰り返す:
   1. `capturer.Capture(region)` → `桁数埋め連番.png` で保存 → 直後に `Bitmap` を `Dispose`
   2. `i < RepeatCount` のときのみ、`Steps` を上から順に実行（Key → `SendKey`、Click → `ClickAt`、Wait → `Delay`）
   3. シーケンス実行後に `AfterSequenceWaitMs` だけ待つ
   4. `progress` に `i / RepeatCount` を通知
   5. 各ステップ・各待機の前後で `ct.ThrowIfCancellationRequested()`
3. 結果（完了 / 中断 / エラー、保存枚数、フォルダパス、エラー内容）を `RunResult` で返す

### スレッドモデル
- `RunAsync` は `Task.Run` で**バックグラウンドスレッド**で実行する（`CopyFromScreen` と `SendInput` はどのスレッドからでも呼べる）
- 緊急停止（WM_HOTKEY）は UI スレッドで受け、`CancellationTokenSource.Cancel()` を呼ぶだけ。実行エンジンが UI スレッドを占有しないため、キー押下が確実に処理される
- 進捗は `IProgress<T>`（UI スレッドの SynchronizationContext 経由）で画面へ反映する

### 中断・異常時の後始末（重要）
- `SendKey` は「修飾キー押下 → 本体キー押下/解放 → 修飾キー逆順解放」を行う。**途中でキャンセル・例外が起きても、押した修飾キーは必ず `finally` で解放する**（Ctrl や Alt が押しっぱなしになる事故を防ぐ）
- 撮影失敗・保存失敗（ディスク満杯・権限など）は、握りつぶさず `RunResult` に原因を入れて**即中断**する。それまでに保存済みの画像は残す
- 例外の種類を `RunResult.Error` に格納し、ログと画面メッセージの両方に出す

## 6. 入力送信（Win32InputSender）

### キー入力
- `SendInput` に `KEYBDINPUT` を渡す。仮想キーコード（`wVk`）と、スキャンコード（`MapVirtualKey`）の両方を設定する
- **拡張キーフラグ（`KEYEVENTF_EXTENDEDKEY`）が必要なキー**: ← ↑ → ↓、Insert、Delete、Home、End、PageUp、PageDown、右 Ctrl/右 Alt、Num Lock、テンキーの Enter / `/`。これらを付け忘れるとテンキーの数字キーとして解釈されるため、キー種別ごとの判定テーブルを持つ
- 押下と解放は1回の `SendInput` 呼び出しでまとめて送る（順序保証のため）
- **登録時のキー取得**: WPF の `PreviewKeyDown` で取得し、`KeyInterop.VirtualKeyFromKey` で仮想キーコードに変換する。Alt 併用時は `Key.System` になるため `SystemKey` を見る。修飾キー単独の押下は確定しない（本体キーが押された時点で確定）
- 緊急停止キー（`Pause/Break`）はステップに登録できないよう、入力時に拒否する

### マウスクリック
- `GetCursorPos`（任意） → `SetCursorPos(x, y)` → `SendInput`（`MOUSEEVENTF_LEFTDOWN` / `LEFTUP` を座標指定なしで現在位置に送る）→ `restoreCursor` が true なら元の位置へ `SetCursorPos`
- `SetCursorPos` は PerMonitorV2 の物理座標で動くため、マルチモニター・高 DPI でもずれない
- クリック直後にカーソルを戻すと、アプリによってはクリックが取りこぼされるため、**戻す前に短い待機（既定 50ms）**を入れる

### マウスホイール
- `WheelStep(Notches, X?, Y?)`。`Notches` は正が奥（上）、負が手前（下）。1 ステップの上限は ±100
- `SendInput`（`MOUSEEVENTF_WHEEL`）で `mouseData = Notches × 120`（`WHEEL_DELTA`）を送る
- X・Y が両方指定されていれば、先に `SetCursorPos` で移動してから回し、`RestoreCursorAfterClick` が true なら元の位置へ戻す（クリックと同じ扱い）
- 設定の読み込み時に、0 ノッチは 1 に、上限超過は上限に、片方だけの位置指定は無効に補正する

### 失敗時
- `SendInput` の戻り値（送れたイベント数）が想定と異なる場合は `Win32Exception`（`Marshal.GetLastWin32Error`）を投げる。別プロセスが昇格（管理者）で動いていて UIPI により拒否された場合もここで検知できるため、**実行を中断してメッセージで通知**する

## 7. 保存（Storage）

### フォルダの確保（`FolderAllocator`）
```
Allocate(parent, name):
    candidate = parent\name
    n = 2
    while Directory.Exists(candidate):
        candidate = parent\name_n ; n++
    Directory.CreateDirectory(candidate)
    return candidate
```
- 既存フォルダには一切書き込まない。多重起動禁止（§9）のため、確認と作成の間の競合は考慮しない
- 戻り値のフルパスを、開始時と完了時の画面表示・ログに使う
- 親フォルダが無ければ作成する。作成に失敗（権限など）したら開始前にエラー表示する

### ファイル名（`FileNameFormatter`）
- `digits = RepeatCount.ToString().Length`
- `fileName = i.ToString($"D{digits}") + ".png"`（例: 50 回 → `01.png` … `50.png`、100 回 → `001.png` … `100.png`、9 回 → `1.png`）

### フォルダ名検証（`FolderNameValidator`）
- 空・空白のみ → 不可
- `Path.GetInvalidFileNameChars()` を含む → 不可
- 末尾が `.` または空白 → 不可（Windows で作成後に名前が変わるため）
- 予約名（`CON`, `PRN`, `AUX`, `NUL`, `COM1`〜`COM9`, `LPT1`〜`LPT9`。拡張子付きも）→ 不可
- パス全体が長すぎる場合（親＋名前＋`_9999`＋`\9999.png` が 259 文字超）→ 不可
- 画面入力時にリアルタイム検証し、エラー内容を入力欄の横に表示する

### 保存処理
- `Bitmap.Save(path, ImageFormat.Png)`。既存ファイルがある場合（別名フォルダを使う設計上は起こらない想定）は上書きせずエラー扱い
- 撮影 → 保存は同期的に直列で行う（1 枚ずつ確実に保存してから次の操作へ進む）

## 8. 画面・オーバーレイ設計

### MainWindow
- 設定項目の編集（要件 §5 の画面構成）。ステップ一覧は `ListBox` + 追加/編集/削除/↑↓ボタン
- 「開始」押下 → 入力検証 → フォルダ確保 → `EmergencyStopHotkey` を登録（**失敗したら開始せずエラー表示**）→ 自身を最小化 → `ProgressWindow` 表示 → `CaptureRunner` 起動
- 完了・中断・エラー時 → `ProgressWindow` を閉じ、ホットキーを解除し、メインウィンドウを復帰して結果（保存枚数・フォルダパス・エラー）を表示。フォルダを開くボタンを用意する
- 実行中は設定欄をすべて無効化する

### RegionPickerWindow（SnipSnap の CaptureOverlayWindow を改造）
- 開始時に仮想スクリーン全体の静止画を撮り、モニター 1 台につき 1 ウィンドウで背景に敷く（SnipSnap と同一）
- **自身のメインウィンドウが写り込まないよう、オーバーレイ表示の直前にメインウィンドウを最小化し、静止画取得前に短い待機（DWM の再描画待ち、250ms 程度。SnipSnap の `Thread.Sleep(250)` と同じ理由）を入れる**
- モード:
  - `Region`: ドラッグで矩形選択、マウスを離すと確定、Esc でキャンセル
  - `Point`: クリックした 1 点を確定。Esc でキャンセル。クリック位置に十字カーソルを表示
- 結果は物理スクリーン座標（`Rectangle` または `Point`）で返す

### ProgressWindow（実行中の進捗表示）
撮影範囲に写り込まないことが最優先。次の**二重の対策**を取る。
1. **配置**: 仮想スクリーン上で撮影範囲と交差しない位置（角）を探して置く。交差しない位置が見つからない場合（範囲が全画面級）は表示しない
2. **キャプチャ除外**: `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` を設定する（Windows 10 2004 以降）。非対応 OS では例外にせず配置のみで対処する

加えて次のウィンドウスタイルを設定する。
- `WS_EX_NOACTIVATE`: フォーカスを奪わない（**対象アプリのフォーカスを維持するため必須**）
- `WS_EX_TRANSPARENT` + `WS_EX_TOOLWINDOW`: クリックを透過させ、Alt+Tab に出さない
- Topmost、`ShowActivated = false`

表示内容: カウントダウン（開始前）、`n / N`、「緊急停止: Pause/Break」。

## 9. 起動・設定・ログ

- **多重起動禁止**: 名前付き Mutex（`Local\ConsecutiveSnap`）で制御。2 つ目は既存ウィンドウを前面に出して終了
- **設定**: 起動時に読み込み → 補正 → 画面に反映。保存は「開始時」と「終了時」。書き込みは一時ファイルに出力してから置き換える（途中で壊れても元ファイルを残す）。読み込みに失敗（JSON 破損など）した場合は既定値で起動し、画面上部に通知を出す。壊れたファイルは `settings.json.broken` に退避する（上書き前に確認不要。削除はしない）
- **ログ**: `%APPDATA%\ConsecutiveSnap\logs\yyyyMMdd_HHmmss.log`。開始時刻、設定の要約、各枚の保存結果、エラー詳細、終了理由を記録する。古いログは自動削除せず、フォルダを開くボタンのみ提供する（v1 では削除機能を作らない）
- **未処理例外**: `DispatcherUnhandledException` / `AppDomain.UnhandledException` で捕捉し、ログに書いてから、実行中ならホットキー解除と状態復帰を行う

## 10. ビルド・配布・依存関係の確認

- 製品プロジェクト（Core / App）は **NuGet パッケージ参照なし**
- 発行: `dotnet publish src/ConsecutiveSnap.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true`
- **発行後に必ず実機で確認する**（宣言ではなく実際の成果物を見る）:
  - 発行フォルダの中身に、想定外の DLL・パッケージが入っていないこと
  - プロジェクト内に `package.json` / `requirements.txt` など、使っていないエコシステムの依存定義が紛れ込んでいないこと
  - テスト専用パッケージ（xUnit 等）が発行物に含まれていないこと
- 配布物は exe 1 つ（必要なら `README` を同梱）

## 11. テスト計画

### 自動テスト（Core）
| 対象 | 内容 |
|---|---|
| `FileNameFormatter` | 回数 1 / 9 / 10 / 99 / 100 / 999 / 1000 / 9999 での桁数と連番 |
| `FolderNameValidator` | 空、禁止文字、末尾ドット/空白、予約名、長すぎるパス |
| `FolderAllocator` | 未存在 → そのまま、存在 → `_2`、`_2` も存在 → `_3`。既存フォルダのファイルが変わらないこと |
| `SettingsStore` | 保存→読込の往復、ステップの多態シリアライズ、壊れた JSON → 既定値＋退避、範囲外の値の補正 |
| `CaptureRunner`（偽の capturer/sender/delay を使用） | 呼び出し回数＝回数分、最終回の後にシーケンスが実行されない、ステップ順序、キャンセル時の即時停止、撮影失敗・保存失敗・送信失敗でのエラー中断、キャンセル後に修飾キー解放が呼ばれること |

### 手動テスト（実機）
| # | 確認内容 |
|---|---|
| 1 | メモ帳／画像ビューアーなどで、キー（→）によるページ送りと撮影が回数分行われる |
| 2 | 「キー → 待機 → クリック」の複合シーケンスが想定順に動く |
| 3 | `Pause/Break` で即時に止まり、Ctrl/Alt/Shift が押しっぱなしにならない |
| 4 | 進捗ウィンドウ・カウントダウンが保存画像に写っていない |
| 5 | 高 DPI（150% 等）・マルチモニター混在で、範囲指定位置と保存画像が一致する |
| 6 | 同名フォルダがある状態で `_2` が作られ、既存画像が変わらない |
| 7 | 対象アプリのフォーカスが、実行中ずっと維持される（進捗ウィンドウがフォーカスを奪わない） |
| 8 | 実際に使う電子書籍／PDF ビューアーで、黒く写らず撮影できる（DRM 保護の有無の確認） |
| 9 | Pause/Break が他アプリに登録済みのとき、開始せずエラー表示になる |
| 10 | 再起動後に範囲・シーケンス・各設定が復元される |

## 12. 実装順序

1. ソリューション作成・Core のデータモデル・Storage（`FileNameFormatter` / `FolderNameValidator` / `FolderAllocator`）とそのテスト
2. `SettingsStore` とテスト
3. `CaptureRunner`（偽実装）とテスト
4. `ScreenCapturer` / `Win32InputSender` の実装
5. `RegionPickerWindow`（範囲・点）、`EmergencyStopHotkey`
6. `MainWindow` / `StepEditorDialog` / `ProgressWindow` の組み立て
7. 実機テスト（§11 手動）→ 修正
8. 発行と成果物の確認（§10）

## 13. 未確定・要検討事項
- `WDA_EXCLUDEFROMCAPTURE` が `CopyFromScreen` による撮影から確実に除外されるかは、**実機で確認する**（効かない場合は配置のみの対策に頼るため、範囲と進捗ウィンドウが重なる場合は進捗ウィンドウを非表示にする）
- 対象アプリが管理者権限で動いている場合は、UIPI により入力が届かない（要件上スコープ外。検知した場合のメッセージのみ用意する）
- 開始前待機後のフォーカスについて: メインウィンドウを最小化すると、直前にアクティブだったウィンドウに戻るのが通常だが、環境により異なる。実機で確認し、必要なら「対象ウィンドウを指定してフォーカスを当てる」機能を将来検討する
