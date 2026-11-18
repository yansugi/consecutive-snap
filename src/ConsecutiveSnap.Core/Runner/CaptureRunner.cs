using System.Drawing;
using System.Drawing.Imaging;
using ConsecutiveSnap.Core.Capture;
using ConsecutiveSnap.Core.Input;
using ConsecutiveSnap.Core.Steps;
using ConsecutiveSnap.Core.Storage;

namespace ConsecutiveSnap.Core.Runner;

/// <summary>実行1回分の入力条件。OutputFolderは確保済み（作成済み）のフォルダ。</summary>
public sealed record RunOptions(
    Rectangle Region,
    IReadOnlyList<ActionStep> Steps,
    int RepeatCount,
    int StartDelaySeconds,
    int AfterSequenceWaitMs,
    bool RestoreCursorAfterClick,
    string OutputFolder);

/// <summary>実行の段階。</summary>
public enum RunPhase
{
    /// <summary>開始前のカウントダウン中。</summary>
    Countdown,
    /// <summary>撮影・操作の繰り返し中。</summary>
    Running
}

/// <summary>進捗通知。Currentは保存済みの枚数。Countdownの間はCountdownRemainingに残り秒数が入る。</summary>
public sealed record RunProgress(RunPhase Phase, int Current, int Total, int CountdownRemaining);

/// <summary>実行の終了状態。</summary>
public enum RunStatus
{
    /// <summary>指定回数すべて撮影した。</summary>
    Completed,
    /// <summary>緊急停止・停止ボタンで中断した。</summary>
    Canceled,
    /// <summary>撮影・保存・入力送信などの失敗で中断した。</summary>
    Failed
}

/// <summary>実行結果。Errorは<see cref="RunStatus.Failed"/>のときの原因。</summary>
public sealed record RunResult(RunStatus Status, int SavedCount, string OutputFolder, Exception? Error);

/// <summary>画像をファイルへ保存するサービス。テスト時に差し替えられるようインターフェース化している。</summary>
public interface IImageSaver
{
    /// <summary>画像をPNGとして保存する。既存ファイルがある場合は上書きせず例外を投げる。</summary>
    void SavePng(Bitmap bitmap, string path);
}

/// <summary>PNG保存の実装。</summary>
public sealed class PngImageSaver : IImageSaver
{
    /// <inheritdoc />
    /// <exception cref="IOException">既存ファイルがある場合、または保存に失敗した場合。</exception>
    public void SavePng(Bitmap bitmap, string path)
    {
        if (File.Exists(path))
        {
            throw new IOException($"同名のファイルが既に存在するため保存できません: {path}");
        }

        try
        {
            bitmap.Save(path, ImageFormat.Png);
        }
        catch (Exception ex) when (ex is not IOException)
        {
            // GDI+の汎用エラー等も、呼び出し側ではIOExceptionとして一括して扱えるよう包み直す
            throw new IOException($"画像を保存できませんでした: {path}（{ex.Message}）", ex);
        }
    }
}

/// <summary>待機サービス。テスト時に即時完了版へ差し替えられるようインターフェース化している。</summary>
public interface IDelay
{
    /// <summary>指定時間だけ待つ。キャンセルされたら<see cref="OperationCanceledException"/>を投げる。</summary>
    Task Wait(TimeSpan time, CancellationToken cancellationToken);
}

/// <summary>Task.Delayによる待機の実装。</summary>
public sealed class TaskDelay : IDelay
{
    /// <inheritdoc />
    public Task Wait(TimeSpan time, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return time <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(time, cancellationToken);
    }
}

/// <summary>
/// 「撮影 → 操作シーケンス → 撮影 → …」を指定回数だけ繰り返す実行エンジン。
/// UIには依存しない。呼び出し側は<c>Task.Run</c>等でバックグラウンド実行すること
/// （緊急停止のキー入力をUIスレッドで確実に受けるため）。
/// </summary>
public sealed class CaptureRunner
{
    private readonly IScreenCapturer _capturer;
    private readonly IInputSender _input;
    private readonly IImageSaver _saver;
    private readonly IDelay _delay;
    private readonly Action<string>? _log;

    /// <summary>依存サービスを受け取って生成する。logは各段階の記録先（任意）。</summary>
    public CaptureRunner(
        IScreenCapturer capturer,
        IInputSender input,
        IImageSaver saver,
        IDelay delay,
        Action<string>? log = null)
    {
        _capturer = capturer ?? throw new ArgumentNullException(nameof(capturer));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _saver = saver ?? throw new ArgumentNullException(nameof(saver));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _log = log;
    }

    /// <summary>
    /// 実行する。例外は外へ投げず、結果は<see cref="RunResult"/>で返す。
    /// 中断・失敗で終わる場合は、修飾キーの押しっぱなしを防ぐため必ず全キーを解放する。
    /// </summary>
    public async Task<RunResult> RunAsync(RunOptions options, IProgress<RunProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        int saved = 0;
        RunStatus status = RunStatus.Failed;
        Exception? error = null;

        try
        {
            ValidateOptions(options);
            Log($"開始: 回数={options.RepeatCount}, 範囲={options.Region}, ステップ数={options.Steps.Count}, 保存先={options.OutputFolder}");

            await CountdownAsync(options, progress, cancellationToken).ConfigureAwait(false);

            for (int i = 1; i <= options.RepeatCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. 撮影して保存する
                string path = Path.Combine(options.OutputFolder, FileNameFormatter.Format(i, options.RepeatCount));
                using (Bitmap bitmap = _capturer.Capture(options.Region))
                {
                    _saver.SavePng(bitmap, path);
                }
                saved = i;
                Log($"保存: {path}");
                progress?.Report(new RunProgress(RunPhase.Running, saved, options.RepeatCount, 0));

                // 最終回の後は操作しない
                if (i == options.RepeatCount)
                {
                    break;
                }

                // 2. 操作シーケンスを上から順に実行する
                foreach (ActionStep step in options.Steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ExecuteStepAsync(step, options, cancellationToken).ConfigureAwait(false);
                }

                // 3. ページ描画待ち
                await _delay.Wait(TimeSpan.FromMilliseconds(options.AfterSequenceWaitMs), cancellationToken).ConfigureAwait(false);
            }

            status = RunStatus.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = RunStatus.Canceled;
            Log("中断: 停止要求を受け付けました。");
        }
        catch (Exception ex)
        {
            status = RunStatus.Failed;
            error = ex;
            Log($"失敗: {ex}");
        }
        finally
        {
            if (status != RunStatus.Completed)
            {
                ReleaseKeysSafely();
            }
        }

        Log($"終了: {status}, 保存枚数={saved}");
        return new RunResult(status, saved, options.OutputFolder, error);
    }

    /// <summary>実行前に入力条件を確認する。不正なら<see cref="ArgumentException"/>。</summary>
    private static void ValidateOptions(RunOptions options)
    {
        if (options.RepeatCount < 1)
        {
            throw new ArgumentException("繰り返し回数は1以上である必要があります。");
        }
        if (options.Region.Width < 1 || options.Region.Height < 1)
        {
            throw new ArgumentException("キャプチャ範囲が不正です。");
        }
        if (string.IsNullOrWhiteSpace(options.OutputFolder))
        {
            throw new ArgumentException("保存先フォルダが指定されていません。");
        }
    }

    /// <summary>開始前のカウントダウン。1秒ごとに残り秒数を通知する。</summary>
    private async Task CountdownAsync(RunOptions options, IProgress<RunProgress>? progress, CancellationToken ct)
    {
        for (int remaining = options.StartDelaySeconds; remaining > 0; remaining--)
        {
            progress?.Report(new RunProgress(RunPhase.Countdown, 0, options.RepeatCount, remaining));
            await _delay.Wait(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    /// <summary>ステップを1つ実行する。</summary>
    private async Task ExecuteStepAsync(ActionStep step, RunOptions options, CancellationToken ct)
    {
        switch (step)
        {
            case KeyStep key:
                _input.SendKey(key.VirtualKey, key.Modifiers);
                break;
            case ClickStep click:
                _input.ClickAt(click.X, click.Y, options.RestoreCursorAfterClick);
                break;
            case WheelStep wheel:
                _input.Wheel(wheel.Notches, wheel.X, wheel.Y, options.RestoreCursorAfterClick);
                break;
            case WaitStep wait:
                await _delay.Wait(TimeSpan.FromMilliseconds(wait.Milliseconds), ct).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"未対応のステップです: {step.GetType().Name}");
        }
    }

    /// <summary>後始末としてキーを解放する。失敗しても元の結果を優先するため例外は外へ出さない。</summary>
    private void ReleaseKeysSafely()
    {
        try
        {
            _input.ReleaseAllKeys();
        }
        catch (Exception ex)
        {
            Log($"キー解放に失敗: {ex.Message}");
        }
    }

    /// <summary>ログ出力。ログ先の失敗で実行を止めない。</summary>
    private void Log(string message)
    {
        try
        {
            _log?.Invoke(message);
        }
        catch (Exception)
        {
            // ログの失敗は実行に影響させない
        }
    }
}
