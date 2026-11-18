using System.Drawing;
using ConsecutiveSnap.Core.Settings;
using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.Core.Tests;

/// <summary>設定の保存・読み込み・補正のテスト。</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ConsecutiveSnapTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public SettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
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
    public void Load_ファイルが無ければ既定値で警告なし()
    {
        var result = new SettingsStore(_path).Load();

        Assert.Null(result.Warning);
        Assert.Equal(10, result.Settings.RepeatCount);
        Assert.Empty(result.Settings.Steps);
    }

    [Fact]
    public void SaveしてLoadすると全項目とステップの型が復元される()
    {
        var store = new SettingsStore(_path);
        var settings = new AppSettings
        {
            CaptureRegion = new RectDto(100, 80, 900, 1200),
            RepeatCount = 50,
            StartDelaySeconds = 3,
            AfterSequenceWaitMs = 700,
            RestoreCursorAfterClick = false,
            ParentFolder = @"C:\out",
            FolderName = "MyBook",
            Steps =
            {
                new KeyStep(0x27, KeyModifiers.Ctrl | KeyModifiers.Shift),
                new WaitStep(300),
                new ClickStep(1200, 800),
                new WheelStep(-5),
                new WheelStep(3, 100, 200)
            }
        };

        store.Save(settings);
        var loaded = store.Load().Settings;

        Assert.Equal(settings.CaptureRegion, loaded.CaptureRegion);
        Assert.Equal(50, loaded.RepeatCount);
        Assert.Equal(3, loaded.StartDelaySeconds);
        Assert.Equal(700, loaded.AfterSequenceWaitMs);
        Assert.False(loaded.RestoreCursorAfterClick);
        Assert.Equal(@"C:\out", loaded.ParentFolder);
        Assert.Equal("MyBook", loaded.FolderName);
        Assert.Equal(settings.Steps, loaded.Steps);
    }

    [Fact]
    public void Save_上書き保存でき一時ファイルは残らない()
    {
        var store = new SettingsStore(_path);
        store.Save(new AppSettings { RepeatCount = 5 });
        store.Save(new AppSettings { RepeatCount = 6 });

        Assert.Equal(6, store.Load().Settings.RepeatCount);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Load_壊れたJSONは既定値と警告を返し元ファイルを退避する()
    {
        File.WriteAllText(_path, "{ これはJSONではない");

        var result = new SettingsStore(_path).Load();

        Assert.NotNull(result.Warning);
        Assert.Equal(10, result.Settings.RepeatCount);
        Assert.False(File.Exists(_path));
        Assert.Single(Directory.GetFiles(_dir, "settings.json.broken-*"));
    }

    [Fact]
    public void Load_範囲外の値は補正され警告が出る()
    {
        File.WriteAllText(_path, "{\"RepeatCount\": 99999, \"StartDelaySeconds\": -5, \"AfterSequenceWaitMs\": 999999}");

        var result = new SettingsStore(_path).Load();

        Assert.Equal(AppSettings.MaxRepeatCount, result.Settings.RepeatCount);
        Assert.Equal(0, result.Settings.StartDelaySeconds);
        Assert.Equal(60000, result.Settings.AfterSequenceWaitMs);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Load_不正なホイールステップは補正される()
    {
        var store = new SettingsStore(_path);
        var settings = new AppSettings();
        settings.Steps.Add(new WheelStep(0));
        settings.Steps.Add(new WheelStep(9999));
        settings.Steps.Add(new WheelStep(-9999, 10, null)); // 片方だけの位置指定は無効にする
        store.Save(settings);

        var result = store.Load();

        Assert.Equal(new WheelStep(1), result.Settings.Steps[0]);
        Assert.Equal(new WheelStep(WheelStep.MaxNotches), result.Settings.Steps[1]);
        Assert.Equal(new WheelStep(-WheelStep.MaxNotches), result.Settings.Steps[2]);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Load_画面外のキャプチャ範囲は破棄して警告する()
    {
        var store = new SettingsStore(_path);
        store.Save(new AppSettings { CaptureRegion = new RectDto(5000, 5000, 100, 100) });

        var result = store.Load(new Rectangle(0, 0, 1920, 1080));

        Assert.Null(result.Settings.CaptureRegion);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public void Load_画面内のキャプチャ範囲は保持される()
    {
        var store = new SettingsStore(_path);
        store.Save(new AppSettings { CaptureRegion = new RectDto(10, 10, 100, 100) });

        var result = store.Load(new Rectangle(0, 0, 1920, 1080));

        Assert.NotNull(result.Settings.CaptureRegion);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Load_負の座標を含むマルチモニター構成でも範囲を保持できる()
    {
        var store = new SettingsStore(_path);
        store.Save(new AppSettings { CaptureRegion = new RectDto(-1500, 100, 800, 600) });

        var result = store.Load(new Rectangle(-1920, 0, 3840, 1080));

        Assert.NotNull(result.Settings.CaptureRegion);
    }
}
