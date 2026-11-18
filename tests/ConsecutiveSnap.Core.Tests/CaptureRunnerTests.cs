using System.Drawing;
using ConsecutiveSnap.Core.Capture;
using ConsecutiveSnap.Core.Input;
using ConsecutiveSnap.Core.Runner;
using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.Core.Tests;

/// <summary>実行エンジンのテスト。撮影・入力・保存・待機はすべて偽物に差し替えて検証する。</summary>
public class CaptureRunnerTests
{
    /// <summary>呼び出しを時系列で記録する共有ログ。</summary>
    private sealed class Timeline : List<string> { }

    private sealed class FakeCapturer : IScreenCapturer
    {
        private readonly Timeline _t;
        public int Count;
        public int? FailAtCount;
        public FakeCapturer(Timeline t) => _t = t;

        public Bitmap Capture(Rectangle screenRect)
        {
            Count++;
            if (FailAtCount == Count) throw new InvalidOperationException("撮影失敗(テスト)");
            _t.Add($"capture{Count}");
            return new Bitmap(2, 2);
        }
    }

    private sealed class FakeSender : IInputSender
    {
        private readonly Timeline _t;
        public int ReleaseCount;
        public bool FailOnKey;
        public bool RestoreSeen;
        public FakeSender(Timeline t) => _t = t;

        public void SendKey(int virtualKey, KeyModifiers modifiers)
        {
            if (FailOnKey) throw new InvalidOperationException("送信失敗(テスト)");
            _t.Add($"key{virtualKey:X}");
        }
        public void ClickAt(int x, int y, bool restoreCursor)
        {
            RestoreSeen = restoreCursor;
            _t.Add($"click{x},{y}");
        }
        public void Wheel(int notches, int? x, int? y, bool restoreCursor)
        {
            RestoreSeen = restoreCursor;
            _t.Add(x.HasValue ? $"wheel{notches}@{x},{y}" : $"wheel{notches}");
        }
        public void ReleaseAllKeys() => ReleaseCount++;
    }

    private sealed class FakeSaver : IImageSaver
    {
        public readonly List<string> Paths = new();
        public int? FailAtCount;
        public void SavePng(Bitmap bitmap, string path)
        {
            if (FailAtCount == Paths.Count + 1) throw new IOException("保存失敗(テスト)");
            Paths.Add(path);
        }
    }

    private sealed class FakeDelay : IDelay
    {
        private readonly Timeline _t;
        public Action<int>? OnWait; // 何回目の待機かを受け取り、テスト中にキャンセル等を起こす
        private int _n;
        public FakeDelay(Timeline t) => _t = t;

        public Task Wait(TimeSpan time, CancellationToken cancellationToken)
        {
            _n++;
            cancellationToken.ThrowIfCancellationRequested();
            _t.Add($"wait{(int)time.TotalMilliseconds}");
            OnWait?.Invoke(_n);
            return Task.CompletedTask;
        }
    }

    private static RunOptions Options(int repeat, IReadOnlyList<ActionStep>? steps = null, int startDelay = 0, int afterWait = 500) =>
        new(new Rectangle(0, 0, 100, 100), steps ?? Array.Empty<ActionStep>(), repeat, startDelay, afterWait, true, @"C:\out");

    private static (CaptureRunner runner, Timeline t, FakeCapturer cap, FakeSender sender, FakeSaver saver, FakeDelay delay) Create()
    {
        var t = new Timeline();
        var cap = new FakeCapturer(t);
        var sender = new FakeSender(t);
        var saver = new FakeSaver();
        var delay = new FakeDelay(t);
        return (new CaptureRunner(cap, sender, saver, delay), t, cap, sender, saver, delay);
    }

    [Fact]
    public async Task 指定回数ぶん撮影して連番で保存する()
    {
        var (runner, _, cap, _, saver, _) = Create();

        var result = await runner.RunAsync(Options(12), null, CancellationToken.None);

        Assert.Equal(RunStatus.Completed, result.Status);
        Assert.Equal(12, result.SavedCount);
        Assert.Equal(12, cap.Count);
        Assert.Equal(@"C:\out\01.png", saver.Paths[0]);
        Assert.Equal(@"C:\out\12.png", saver.Paths[11]);
    }

    [Fact]
    public async Task 最終回の撮影後にはシーケンスも待機も実行されない()
    {
        var (runner, t, _, _, _, _) = Create();
        var steps = new ActionStep[] { new KeyStep(0x27, KeyModifiers.None) };

        await runner.RunAsync(Options(2, steps, afterWait: 500), null, CancellationToken.None);

        // 撮影1 → キー → 待機 → 撮影2 で終わる（撮影2の後には何も無い）
        Assert.Equal(new[] { "capture1", "key27", "wait500", "capture2" }, t);
    }

    [Fact]
    public async Task ステップは登録順に実行され操作後待機が最後に入る()
    {
        var (runner, t, _, _, _, _) = Create();
        var steps = new ActionStep[]
        {
            new KeyStep(0x27, KeyModifiers.None),
            new WaitStep(300),
            new ClickStep(1200, 800)
        };

        await runner.RunAsync(Options(2, steps, afterWait: 500), null, CancellationToken.None);

        Assert.Equal(
            new[] { "capture1", "key27", "wait300", "click1200,800", "wait500", "capture2" },
            t);
    }

    [Fact]
    public async Task ホイールステップも登録順に実行される()
    {
        var (runner, t, _, _, _, _) = Create();
        var steps = new ActionStep[]
        {
            new WheelStep(-3),
            new WaitStep(100),
            new WheelStep(2, 640, 360)
        };

        await runner.RunAsync(Options(2, steps, afterWait: 500), null, CancellationToken.None);

        Assert.Equal(
            new[] { "capture1", "wheel-3", "wait100", "wheel2@640,360", "wait500", "capture2" },
            t);
    }

    [Fact]
    public async Task 回数1ならシーケンスを一度も実行しない()
    {
        var (runner, t, _, _, _, _) = Create();
        var steps = new ActionStep[] { new KeyStep(0x27, KeyModifiers.None) };

        var result = await runner.RunAsync(Options(1, steps), null, CancellationToken.None);

        Assert.Equal(RunStatus.Completed, result.Status);
        Assert.Equal(new[] { "capture1" }, t);
    }

    [Fact]
    public async Task 開始前カウントダウンが秒数ぶん通知される()
    {
        var (runner, _, _, _, _, _) = Create();
        var reports = new List<RunProgress>();
        var progress = new SyncProgress<RunProgress>(reports.Add);

        await runner.RunAsync(Options(1, startDelay: 3), progress, CancellationToken.None);

        var countdown = reports.Where(r => r.Phase == RunPhase.Countdown).Select(r => r.CountdownRemaining).ToArray();
        Assert.Equal(new[] { 3, 2, 1 }, countdown);
    }

    [Fact]
    public async Task 進捗は保存枚数で通知される()
    {
        var (runner, _, _, _, _, _) = Create();
        var reports = new List<RunProgress>();

        await runner.RunAsync(Options(3), new SyncProgress<RunProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(new[] { 1, 2, 3 }, reports.Where(r => r.Phase == RunPhase.Running).Select(r => r.Current).ToArray());
    }

    [Fact]
    public async Task 実行中にキャンセルすると即座に止まりキーを解放する()
    {
        var (runner, _, cap, sender, saver, delay) = Create();
        using var cts = new CancellationTokenSource();
        // 2回目の待機（=2枚目の撮影後の操作後待機の前後）でキャンセルを発生させる
        delay.OnWait = n => { if (n == 2) cts.Cancel(); };
        var steps = new ActionStep[] { new KeyStep(0x27, KeyModifiers.Ctrl) };

        var result = await runner.RunAsync(Options(10, steps), null, cts.Token);

        Assert.Equal(RunStatus.Canceled, result.Status);
        Assert.True(result.SavedCount < 10);
        Assert.Equal(result.SavedCount, saver.Paths.Count);
        Assert.Equal(result.SavedCount, cap.Count);
        Assert.Equal(1, sender.ReleaseCount);
    }

    [Fact]
    public async Task 開始前にキャンセル済みなら一枚も撮らない()
    {
        var (runner, _, cap, sender, _, _) = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await runner.RunAsync(Options(5), null, cts.Token);

        Assert.Equal(RunStatus.Canceled, result.Status);
        Assert.Equal(0, cap.Count);
        Assert.Equal(1, sender.ReleaseCount);
    }

    [Fact]
    public async Task 撮影失敗はエラーで中断し保存済み分は残る()
    {
        var (runner, _, cap, sender, saver, _) = Create();
        cap.FailAtCount = 3;

        var result = await runner.RunAsync(Options(5), null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(2, result.SavedCount);
        Assert.Equal(2, saver.Paths.Count);
        Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Equal(1, sender.ReleaseCount);
    }

    [Fact]
    public async Task 保存失敗はエラーで中断する()
    {
        var (runner, _, _, _, saver, _) = Create();
        saver.FailAtCount = 2;

        var result = await runner.RunAsync(Options(5), null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(1, result.SavedCount);
        Assert.IsType<IOException>(result.Error);
    }

    [Fact]
    public async Task 入力送信失敗はエラーで中断しキーを解放する()
    {
        var (runner, _, _, sender, _, _) = Create();
        sender.FailOnKey = true;
        var steps = new ActionStep[] { new KeyStep(0x27, KeyModifiers.None) };

        var result = await runner.RunAsync(Options(5, steps), null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(1, result.SavedCount);
        Assert.Equal(1, sender.ReleaseCount);
    }

    [Fact]
    public async Task 正常完了ではキー解放を呼ばない()
    {
        var (runner, _, _, sender, _, _) = Create();

        await runner.RunAsync(Options(3), null, CancellationToken.None);

        Assert.Equal(0, sender.ReleaseCount);
    }

    [Fact]
    public async Task クリック後カーソルを戻す設定が伝わる()
    {
        var (runner, _, _, sender, _, _) = Create();
        var steps = new ActionStep[] { new ClickStep(10, 20) };

        await runner.RunAsync(Options(2, steps), null, CancellationToken.None);

        Assert.True(sender.RestoreSeen);
    }

    [Fact]
    public async Task 不正な条件は失敗として返り例外は外へ出ない()
    {
        var (runner, _, cap, _, _, _) = Create();

        var result = await runner.RunAsync(Options(0), null, CancellationToken.None);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.IsType<ArgumentException>(result.Error);
        Assert.Equal(0, cap.Count);
    }

    /// <summary>テスト用の同期Progress（Progress&lt;T&gt;は非同期にポストされ順序検証が不安定になるため）。</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SyncProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}

/// <summary>キー関連の知識（拡張キー判定・登録可否）のテスト。</summary>
public class KeyCatalogTests
{
    [Theory]
    [InlineData(0x25)] // ←
    [InlineData(0x27)] // →
    [InlineData(0x21)] // PageUp
    [InlineData(0x22)] // PageDown
    [InlineData(0x2E)] // Delete
    public void 矢印やPageキーは拡張キー(int vk)
    {
        Assert.True(KeyCatalog.IsExtendedKey(vk));
    }

    [Theory]
    [InlineData(0x20)] // Space
    [InlineData(0x0D)] // Enter
    [InlineData(0x41)] // A
    public void 通常キーは拡張キーではない(int vk)
    {
        Assert.False(KeyCatalog.IsExtendedKey(vk));
    }

    [Fact]
    public void PauseBreakは操作として登録できない()
    {
        Assert.NotNull(KeyCatalog.ValidateKeyStep(KeyCatalog.PauseVirtualKey));
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0x11)]
    [InlineData(0x12)]
    [InlineData(0x5B)]
    public void 修飾キー単独は登録できない(int vk)
    {
        Assert.NotNull(KeyCatalog.ValidateKeyStep(vk));
    }

    [Fact]
    public void 通常キーは登録できる()
    {
        Assert.Null(KeyCatalog.ValidateKeyStep(0x27));
    }

    [Fact]
    public void 修飾キーは押下順にCtrl_Alt_Shift_Winで返る()
    {
        var keys = KeyCatalog.GetModifierVirtualKeys(KeyModifiers.Win | KeyModifiers.Shift | KeyModifiers.Ctrl | KeyModifiers.Alt);

        Assert.Equal(new[] { KeyCatalog.VkLControl, KeyCatalog.VkLMenu, KeyCatalog.VkLShift, KeyCatalog.VkLWin }, keys);
    }
}
