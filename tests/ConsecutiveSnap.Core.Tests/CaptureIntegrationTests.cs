using System.Drawing;
using ConsecutiveSnap.Core.Capture;
using ConsecutiveSnap.Core.Runner;

namespace ConsecutiveSnap.Core.Tests;

/// <summary>
/// 実際の画面撮影とPNG保存の統合テスト。デスクトップセッションがある環境での実行を前提とする。
/// （入力送信は、実行中のアクティブなウィンドウへ誤送信しうるため、自動テストには含めない）
/// </summary>
public class CaptureIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ConsecutiveSnapTests_" + Guid.NewGuid().ToString("N"));

    public CaptureIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        // 自分が作成した一時フォルダのみを後始末する
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void 仮想スクリーンのサイズを取得できる()
    {
        Rectangle bounds = ScreenCapturer.GetVirtualScreenBounds();

        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void 指定した矩形のサイズで撮影できる()
    {
        Rectangle vs = ScreenCapturer.GetVirtualScreenBounds();
        var rect = new Rectangle(vs.X, vs.Y, 64, 48);

        using Bitmap bitmap = new ScreenCapturer().Capture(rect);

        Assert.Equal(64, bitmap.Width);
        Assert.Equal(48, bitmap.Height);
    }

    [Fact]
    public void 不正なサイズの矩形は例外()
    {
        Assert.Throws<ArgumentException>(() => new ScreenCapturer().Capture(new Rectangle(0, 0, 0, 10)));
    }

    [Fact]
    public async Task 実機で撮影して連番PNGとして保存できる()
    {
        Rectangle vs = ScreenCapturer.GetVirtualScreenBounds();
        var options = new RunOptions(
            new Rectangle(vs.X, vs.Y, 32, 32), Array.Empty<Core.Steps.ActionStep>(),
            RepeatCount: 12, StartDelaySeconds: 0, AfterSequenceWaitMs: 0, RestoreCursorAfterClick: false, OutputFolder: _dir);
        var runner = new CaptureRunner(new ScreenCapturer(), new NoopInputSender(), new PngImageSaver(), new TaskDelay());

        RunResult result = await runner.RunAsync(options, null, CancellationToken.None);

        Assert.Equal(RunStatus.Completed, result.Status);
        string[] files = Directory.GetFiles(_dir, "*.png").Select(Path.GetFileName).OrderBy(n => n).ToArray()!;
        Assert.Equal(12, files.Length);
        Assert.Equal("01.png", files[0]);
        Assert.Equal("12.png", files[11]);

        // 保存されたPNGが画像として読めて、指定サイズであること
        using var image = Image.FromFile(Path.Combine(_dir, "01.png"));
        Assert.Equal(32, image.Width);
        Assert.Equal(32, image.Height);
    }

    [Fact]
    public void 同名ファイルがあるとPNG保存は上書きせず例外()
    {
        string path = Path.Combine(_dir, "01.png");
        File.WriteAllText(path, "original");

        using var bitmap = new Bitmap(4, 4);
        Assert.Throws<IOException>(() => new PngImageSaver().SavePng(bitmap, path));
        Assert.Equal("original", File.ReadAllText(path));
    }

    /// <summary>何も送らない入力送信（統合テストで実際のキー入力を送らないため）。</summary>
    private sealed class NoopInputSender : Core.Input.IInputSender
    {
        public void SendKey(int virtualKey, Core.Steps.KeyModifiers modifiers) { }
        public void ClickAt(int x, int y, bool restoreCursor) { }
        public void Wheel(int notches, int? x, int? y, bool restoreCursor) { }
        public void ReleaseAllKeys() { }
    }
}
