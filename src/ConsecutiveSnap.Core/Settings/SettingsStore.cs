using System.Drawing;
using System.Text.Json;
using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.Core.Settings;

/// <summary>設定の読み込み結果。Warningは利用者へ通知すべき内容（無ければnull）。</summary>
public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

/// <summary>
/// 設定ファイル（JSON）の読み書き。書き込みは一時ファイル経由で置き換え、途中で壊れても元ファイルを残す。
/// 読み込み失敗時は既定値で続行し、壊れたファイルは削除せず別名で退避する。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;

    /// <summary>設定ファイルのパスを指定して生成する。</summary>
    public SettingsStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>既定の設定ファイルパス（%APPDATA%\ConsecutiveSnap\settings.json）を返す。</summary>
    public static string GetDefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ConsecutiveSnap", "settings.json");
    }

    /// <summary>
    /// 設定を読み込む。ファイルが無ければ既定値（警告なし）。
    /// 読めない・壊れている場合は既定値と警告メッセージを返し、壊れたファイルは退避する。
    /// virtualScreenを渡すと、キャプチャ範囲が画面外になっていないかも確認する。
    /// </summary>
    public SettingsLoadResult Load(Rectangle? virtualScreen = null)
    {
        if (!File.Exists(_path))
        {
            return new SettingsLoadResult(new AppSettings(), null);
        }

        AppSettings? loaded;
        try
        {
            string json = File.ReadAllText(_path);
            loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            string? backup = TryQuarantine();
            string where = backup is null ? "" : $"（元のファイルは「{backup}」に退避しました）";
            return new SettingsLoadResult(
                new AppSettings(),
                $"設定ファイルを読み込めなかったため、初期設定で起動しました{where}。原因: {ex.Message}");
        }

        if (loaded is null)
        {
            string? backup = TryQuarantine();
            string where = backup is null ? "" : $"（元のファイルは「{backup}」に退避しました）";
            return new SettingsLoadResult(new AppSettings(), $"設定ファイルの内容が空だったため、初期設定で起動しました{where}。");
        }

        List<string> warnings = Sanitize(loaded, virtualScreen);
        return new SettingsLoadResult(loaded, warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings));
    }

    /// <summary>
    /// 設定を保存する。一時ファイルに書いてから置き換える。
    /// </summary>
    /// <exception cref="IOException">保存に失敗した場合（原因を内包）。</exception>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string tempPath = _path + ".tmp";
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, null);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            throw new IOException($"設定を保存できませんでした: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 読み込んだ設定の値を許容範囲に補正する。補正した内容は警告として返す。
    /// 範囲外のキャプチャ範囲は破棄して再指定を促す（モニター構成が変わった場合に備える）。
    /// </summary>
    public static List<string> Sanitize(AppSettings s, Rectangle? virtualScreen = null)
    {
        var warnings = new List<string>();

        s.Steps ??= new List<ActionStep>();
        if (s.Steps.RemoveAll(step => step is null) > 0)
        {
            warnings.Add("不正な操作ステップを取り除きました。");
        }

        int repeat = Math.Clamp(s.RepeatCount, AppSettings.MinRepeatCount, AppSettings.MaxRepeatCount);
        if (repeat != s.RepeatCount)
        {
            warnings.Add($"繰り返し回数を{repeat}に補正しました。");
            s.RepeatCount = repeat;
        }

        s.StartDelaySeconds = Math.Clamp(s.StartDelaySeconds, 0, 600);
        s.AfterSequenceWaitMs = Math.Clamp(s.AfterSequenceWaitMs, 0, 60000);

        for (int i = 0; i < s.Steps.Count; i++)
        {
            if (s.Steps[i] is WaitStep wait)
            {
                int clamped = Math.Clamp(wait.Milliseconds, 0, 600000);
                if (clamped != wait.Milliseconds)
                {
                    s.Steps[i] = wait with { Milliseconds = clamped };
                    warnings.Add("待機ステップの時間を補正しました。");
                }
            }
            else if (s.Steps[i] is WheelStep wheel)
            {
                // 0ノッチは無意味なので1に、上限超過は上限に補正する。位置は片方だけの指定を無効にする。
                int notches = wheel.Notches == 0 ? 1 : Math.Clamp(wheel.Notches, -WheelStep.MaxNotches, WheelStep.MaxNotches);
                int? x = wheel.HasPosition ? wheel.X : null;
                int? y = wheel.HasPosition ? wheel.Y : null;
                if (notches != wheel.Notches || x != wheel.X || y != wheel.Y)
                {
                    s.Steps[i] = new WheelStep(notches, x, y);
                    warnings.Add("ホイールステップの値を補正しました。");
                }
            }
        }

        s.ParentFolder ??= "";
        s.FolderName ??= "";

        if (s.CaptureRegion is { } r)
        {
            bool invalidSize = r.Width < 1 || r.Height < 1;
            bool outside = virtualScreen is { } vs && !vs.Contains(new Rectangle(r.X, r.Y, r.Width, r.Height));
            if (invalidSize || outside)
            {
                s.CaptureRegion = null;
                warnings.Add("保存されていたキャプチャ範囲が現在の画面構成に合わないため、範囲を指定し直してください。");
            }
        }

        return warnings;
    }

    /// <summary>
    /// 壊れた設定ファイルを、削除せずタイムスタンプ付きの別名へ移す。失敗したらnull。
    /// </summary>
    private string? TryQuarantine()
    {
        try
        {
            string backup = $"{_path}.broken-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(_path, backup);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
