using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ConsecutiveSnap.Core.Capture;
using ConsecutiveSnap.Core.Input;
using ConsecutiveSnap.Core.Runner;
using ConsecutiveSnap.Core.Settings;
using ConsecutiveSnap.Core.Steps;
using ConsecutiveSnap.Core.Storage;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ConsecutiveSnap.App;

/// <summary>ステップ一覧の1行。表示用の文字列を持つ。</summary>
internal sealed record StepItem(ActionStep Step)
{
    /// <summary>種別名（バッジ表示用）。</summary>
    public string Kind => StepDisplay.Kind(Step);

    /// <summary>詳細（キー名・座標・ノッチ数など）。</summary>
    public string Detail => StepDisplay.Detail(Step);
}

/// <summary>
/// メインウィンドウ。設定の入力・ステップの編集・実行の開始と停止・結果の表示を担当する。
/// 実行そのものは<see cref="CaptureRunner"/>（Core）に任せ、このクラスは前後の準備と後始末を行う。
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new(SettingsStore.GetDefaultPath());
    private readonly ObservableCollection<StepItem> _steps = new();
    private DrawingRectangle? _region;
    private bool _running;
    private CancellationTokenSource? _cts;
    private string? _lastOutputFolder;

    /// <summary>この起動分のログ。</summary>
    internal AppLog Log { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = App.MainWindowTitle;
        StepList.ItemsSource = _steps;

        // 内容が画面より高くなっても収まるよう、作業領域の高さを上限にする（超える分はスクロール）
        MaxHeight = SystemParameters.WorkArea.Height;

        LoadSettings();
        UpdateRegionText();
        UpdateFileNamePreview();
    }

    // ───────────────────────── 設定の読み書き ─────────────────────────

    /// <summary>設定を読み込んで画面に反映する。読み込み時の補正・失敗は上部に通知する。</summary>
    private void LoadSettings()
    {
        SettingsLoadResult result;
        try
        {
            result = _store.Load(ScreenCapturer.GetVirtualScreenBounds());
        }
        catch (Exception ex)
        {
            // Load内で想定外の例外が出ても、既定値で起動できるようにする
            Log.Write($"設定の読み込みで例外: {ex}");
            result = new SettingsLoadResult(new AppSettings(), $"設定を読み込めなかったため、初期設定で起動しました。原因: {ex.Message}");
        }

        AppSettings s = result.Settings;
        _region = s.CaptureRegion is { } r ? new DrawingRectangle(r.X, r.Y, r.Width, r.Height) : null;
        foreach (ActionStep step in s.Steps)
        {
            _steps.Add(new StepItem(step));
        }
        AfterWaitBox.Text = s.AfterSequenceWaitMs.ToString();
        RestoreCursorCheck.IsChecked = s.RestoreCursorAfterClick;
        RepeatBox.Text = s.RepeatCount.ToString();
        StartDelayBox.Text = s.StartDelaySeconds.ToString();
        ParentBox.Text = s.ParentFolder;
        FolderNameBox.Text = s.FolderName;

        if (result.Warning is not null)
        {
            ShowWarning(result.Warning);
            Log.Write($"設定読み込み通知: {result.Warning}");
        }
    }

    /// <summary>画面の入力値から設定を組み立てる。不正があれば利用者向けのエラーメッセージを返す。</summary>
    private (AppSettings? Settings, string? Error) ReadSettingsFromUi()
    {
        if (_region is not { } region)
        {
            return (null, "撮影範囲を指定してください。");
        }
        if (!ScreenCapturer.GetVirtualScreenBounds().Contains(region))
        {
            return (null, "撮影範囲が現在の画面の外にあります。範囲を指定し直してください。");
        }

        if (!int.TryParse(RepeatBox.Text, out int repeat) || repeat < AppSettings.MinRepeatCount || repeat > AppSettings.MaxRepeatCount)
        {
            return (null, $"繰り返し回数は{AppSettings.MinRepeatCount}〜{AppSettings.MaxRepeatCount}の整数で入力してください。");
        }
        if (!int.TryParse(StartDelayBox.Text, out int startDelay) || startDelay < 0 || startDelay > 600)
        {
            return (null, "開始前の待機は0〜600秒の整数で入力してください。");
        }
        if (!int.TryParse(AfterWaitBox.Text, out int afterWait) || afterWait < 0 || afterWait > 60000)
        {
            return (null, "操作後の待機は0〜60000ミリ秒の整数で入力してください。");
        }

        string? folderError = FolderNameValidator.Validate(ParentBox.Text, FolderNameBox.Text);
        if (folderError is not null)
        {
            return (null, folderError);
        }

        var settings = new AppSettings
        {
            CaptureRegion = new RectDto(region.X, region.Y, region.Width, region.Height),
            Steps = _steps.Select(i => i.Step).ToList(),
            RepeatCount = repeat,
            StartDelaySeconds = startDelay,
            AfterSequenceWaitMs = afterWait,
            RestoreCursorAfterClick = RestoreCursorCheck.IsChecked == true,
            ParentFolder = ParentBox.Text.Trim(),
            FolderName = FolderNameBox.Text.Trim()
        };
        return (settings, null);
    }

    /// <summary>
    /// 画面の現在の入力値を、検証を通らなくても可能な範囲で保存する（終了時用）。
    /// 数値が不正な項目は既定値のまま保存する。失敗してもログに残すだけで、終了は妨げない。
    /// </summary>
    private void SaveSettingsBestEffort()
    {
        try
        {
            var s = new AppSettings
            {
                CaptureRegion = _region is { } r ? new RectDto(r.X, r.Y, r.Width, r.Height) : null,
                Steps = _steps.Select(i => i.Step).ToList(),
                RestoreCursorAfterClick = RestoreCursorCheck.IsChecked == true,
                ParentFolder = ParentBox.Text.Trim(),
                FolderName = FolderNameBox.Text.Trim()
            };
            if (int.TryParse(RepeatBox.Text, out int repeat)) s.RepeatCount = repeat;
            if (int.TryParse(StartDelayBox.Text, out int delay)) s.StartDelaySeconds = delay;
            if (int.TryParse(AfterWaitBox.Text, out int wait)) s.AfterSequenceWaitMs = wait;
            SettingsStore.Sanitize(s);
            _store.Save(s);
        }
        catch (Exception ex)
        {
            Log.Write($"設定の保存に失敗: {ex.Message}");
        }
    }

    // ───────────────────────── 画面表示の更新 ─────────────────────────

    private void ShowWarning(string message)
    {
        WarningText.Text = message;
        WarningBorder.Visibility = Visibility.Visible;
    }

    private void UpdateRegionText()
    {
        RegionText.Text = _region is { } r
            ? $"X:{r.X}  Y:{r.Y}  幅:{r.Width}  高さ:{r.Height}"
            : "（未指定）";
    }

    /// <summary>回数・保存先・フォルダ名から、保存されるファイル名の例とエラーを表示する。</summary>
    private void UpdateFileNamePreview()
    {
        // InitializeComponent中にTextChangedが発火した場合は、まだ部品が無いので何もしない
        if (FileNamePreview is null || FolderError is null || RepeatBox is null || ParentBox is null || FolderNameBox is null)
        {
            return;
        }

        FolderError.Text = "";
        string name = FolderNameBox.Text.Trim();

        if (int.TryParse(RepeatBox.Text, out int repeat) && repeat >= AppSettings.MinRepeatCount && repeat <= AppSettings.MaxRepeatCount)
        {
            string first = FileNameFormatter.Format(1, repeat);
            string last = FileNameFormatter.Format(repeat, repeat);
            FileNamePreview.Text = $"保存例:  {(name.Length == 0 ? "（フォルダ名）" : name)}\\{first} 〜 {last}";
        }
        else
        {
            FileNamePreview.Text = "";
        }

        if (name.Length > 0 || ParentBox.Text.Length > 0)
        {
            FolderError.Text = FolderNameValidator.Validate(ParentBox.Text, name) ?? "";
            if (name.Length == 0)
            {
                FolderError.Text = ""; // 未入力は開始時に検証する（入力途中で赤字を出さない）
            }
        }
    }

    private void RepeatOrNameChanged(object sender, TextChangedEventArgs e) => UpdateFileNamePreview();

    /// <summary>実行中かどうかに応じて、入力欄と開始・停止ボタンの有効・無効を切り替える。</summary>
    private void SetRunningUi(bool running)
    {
        InputPanel.IsEnabled = !running;
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
    }

    // ───────────────────────── 範囲・座標の指定 ─────────────────────────

    /// <summary>
    /// 自分のウィンドウが写り込まないよう最小化し、DWMの再描画を待ってから指定処理を実行して、元に戻す。
    /// ダイアログから呼ぶ場合、オーナーである本ウィンドウを最小化すれば、所有されたダイアログも一緒に隠れる。
    /// </summary>
    private async Task<T?> PickWithWindowHiddenAsync<T>(Func<T?> pick, Window? dialog = null) where T : struct
    {
        WindowState = WindowState.Minimized;
        // 最小化アニメーションが終わるまで待つ（SnipSnapの250ms待機と同じ理由。余裕を見て300ms）
        await Task.Delay(300);
        try
        {
            return pick();
        }
        finally
        {
            WindowState = WindowState.Normal;
            Activate();
            dialog?.Activate();
        }
    }

    private async void SelectRegionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DrawingRectangle? picked = await PickWithWindowHiddenAsync(RegionPicker.PickRegion);
            if (picked is { } region)
            {
                _region = region;
                UpdateRegionText();
                Log.Write($"撮影範囲を指定: {region}");
            }
        }
        catch (Exception ex)
        {
            // async voidのため、例外はここで必ず受け止めて利用者に伝える
            Log.Write($"範囲指定に失敗: {ex}");
            MessageBox.Show(this, $"範囲を指定できませんでした。\n{ex.Message}", App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>ステップ編集ダイアログから呼ばれる、クリック位置の指定処理。</summary>
    private Task<DrawingPoint?> PickPointForDialogAsync(Window dialog) =>
        PickWithWindowHiddenAsync(RegionPicker.PickPoint, dialog);

    // ───────────────────────── ステップの編集 ─────────────────────────

    private void AddStepButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new StepEditorDialog(null, PickPointForDialogAsync) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _steps.Add(new StepItem(dialog.Result));
            StepList.SelectedIndex = _steps.Count - 1;
        }
    }

    private void EditStepButton_Click(object sender, RoutedEventArgs e) => EditSelectedStep();

    private void StepList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedStep();

    /// <summary>選択中のステップを編集ダイアログで編集して置き換える。</summary>
    private void EditSelectedStep()
    {
        int index = StepList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        var dialog = new StepEditorDialog(_steps[index].Step, PickPointForDialogAsync) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _steps[index] = new StepItem(dialog.Result);
            StepList.SelectedIndex = index;
        }
    }

    private void DeleteStepButton_Click(object sender, RoutedEventArgs e)
    {
        int index = StepList.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _steps.RemoveAt(index);
        StepList.SelectedIndex = Math.Min(index, _steps.Count - 1);
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelectedStep(-1);

    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelectedStep(+1);

    /// <summary>選択中のステップを上下に1つ動かす。</summary>
    private void MoveSelectedStep(int delta)
    {
        int index = StepList.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _steps.Count)
        {
            return;
        }

        _steps.Move(index, target);
        StepList.SelectedIndex = target;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "保存先の親フォルダを選択",
            InitialDirectory = Directory.Exists(ParentBox.Text) ? ParentBox.Text : ""
        };
        if (dialog.ShowDialog(this) == true)
        {
            ParentBox.Text = dialog.FolderName;
        }
    }

    // ───────────────────────── 実行 ─────────────────────────

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await StartRunAsync();
        }
        catch (Exception ex)
        {
            // async voidのため、例外はここで必ず受け止めて利用者に伝える（後始末はStartRunAsync側のfinallyで済んでいる）
            Log.Write($"実行開始処理で例外: {ex}");
            MessageBox.Show(this, $"実行中にエラーが発生しました。\n{ex.Message}", App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    /// <summary>
    /// 検証 → 緊急停止キー登録 → 保存フォルダ確保 → 最小化 → 進捗ウィンドウ表示 → 実行 → 後始末 の一連を行う。
    /// 緊急停止キーを登録できない場合は、止める手段が無いまま走らないよう開始しない。
    /// </summary>
    private async Task StartRunAsync()
    {
        if (_running)
        {
            return;
        }

        (AppSettings? settings, string? error) = ReadSettingsFromUi();
        if (settings is null)
        {
            MessageBox.Show(this, error, App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        SaveSettingsBestEffort();

        var cts = new CancellationTokenSource();
        EmergencyStopHotkey? hotkey = null;
        ProgressWindow? progressWindow = null;

        try
        {
            // 1. 緊急停止キー（Pause/Break）を登録する。フォルダ作成より前に行い、失敗時に空フォルダを残さない。
            hotkey = new EmergencyStopHotkey(() => cts.Cancel());
            if (!hotkey.IsRegistered)
            {
                MessageBox.Show(this,
                    "緊急停止キー（Pause/Break）を登録できませんでした。\n他のアプリが同じキーを使用している可能性があります。\n" +
                    "止める手段が無いまま実行しないよう、開始を中止しました。",
                    App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 保存フォルダを新規作成する（同名があれば別名）
            string outputFolder;
            try
            {
                outputFolder = FolderAllocator.Allocate(settings.ParentFolder, settings.FolderName);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                Log.Write($"保存フォルダ作成に失敗: {ex.Message}");
                MessageBox.Show(this, ex.Message, App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _lastOutputFolder = outputFolder;
            Log.Write($"実行開始 保存先={outputFolder}");

            var region = new DrawingRectangle(
                settings.CaptureRegion!.X, settings.CaptureRegion.Y, settings.CaptureRegion.Width, settings.CaptureRegion.Height);
            var options = new RunOptions(
                region, settings.Steps, settings.RepeatCount, settings.StartDelaySeconds,
                settings.AfterSequenceWaitMs, settings.RestoreCursorAfterClick, outputFolder);

            // 3. 実行中の表示に切り替える。自分のウィンドウは撮影に写らないよう最小化する。
            _cts = cts;
            _running = true;
            SetRunningUi(true);
            OpenFolderButton.Visibility = Visibility.Collapsed;
            ResultText.Text = $"実行中...  保存先: {outputFolder}";

            WindowState = WindowState.Minimized;
            await Task.Delay(300); // 最小化アニメーションの完了待ち（最初の撮影に写り込ませないため）

            progressWindow = new ProgressWindow();
            bool progressVisible = progressWindow.ShowOutside(region);
            if (!progressVisible)
            {
                Log.Write("撮影範囲と重ならない位置が無いため、進捗ウィンドウは表示しません。");
            }

            var progress = new Progress<RunProgress>(p =>
            {
                if (progressVisible)
                {
                    progressWindow.UpdateProgress(p);
                }
                ResultText.Text = p.Phase == RunPhase.Countdown
                    ? $"開始まで {p.CountdownRemaining} 秒..."
                    : $"撮影中 {p.Current} / {p.Total}  保存先: {outputFolder}";
            });

            // 4. バックグラウンドで実行する（緊急停止キーをUIスレッドで受けられるようにするため）
            var runner = new CaptureRunner(new ScreenCapturer(), new Win32InputSender(), new PngImageSaver(), new TaskDelay(), Log.Write);
            RunResult result = await Task.Run(() => runner.RunAsync(options, progress, cts.Token));

            ShowResult(result, settings.RepeatCount);
        }
        finally
        {
            // 5. 後始末。ホットキー解除 → 進捗ウィンドウを閉じる → ウィンドウ復帰
            hotkey?.Dispose();
            progressWindow?.Close();
            _cts = null;
            cts.Dispose();
            _running = false;
            SetRunningUi(false);
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }
    }

    /// <summary>実行結果を画面に表示する。失敗時はメッセージも出す。</summary>
    private void ShowResult(RunResult result, int total)
    {
        OpenFolderButton.Visibility = Visibility.Visible;

        switch (result.Status)
        {
            case RunStatus.Completed:
                ResultText.Text = $"完了しました。{result.SavedCount}枚を保存しました。\n保存先: {result.OutputFolder}";
                break;
            case RunStatus.Canceled:
                ResultText.Text = $"中断しました。{result.SavedCount} / {total} 枚を保存済みです。\n保存先: {result.OutputFolder}";
                break;
            default:
                ResultText.Text = $"エラーで中断しました。{result.SavedCount} / {total} 枚を保存済みです。\n保存先: {result.OutputFolder}";
                string message = result.Error?.Message ?? "不明なエラー";
                // メインウィンドウを復帰させてから通知する
                WindowState = WindowState.Normal;
                MessageBox.Show(this, $"エラーのため中断しました。\n\n{message}\n\n保存済みの画像は残っています。",
                    App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                break;
        }
    }

    // ───────────────────────── フォルダを開く・終了 ─────────────────────────

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenInExplorer(_lastOutputFolder);

    private void OpenLogButton_Click(object sender, RoutedEventArgs e) => OpenInExplorer(AppLog.GetLogDirectory());

    /// <summary>エクスプローラーでフォルダを開く。無い・開けない場合は利用者に伝える。</summary>
    private void OpenInExplorer(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "フォルダがまだ存在しません。", App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Write($"フォルダを開けませんでした: {ex.Message}");
            MessageBox.Show(this, $"フォルダを開けませんでした。\n{ex.Message}", App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>終了時処理。実行中は閉じさせず、通常時は設定を保存する。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_running)
        {
            e.Cancel = true;
            MessageBox.Show(this, "実行中です。先に停止（Pause/Break キー）してください。",
                App.MainWindowTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveSettingsBestEffort();
        base.OnClosing(e);
    }
}
