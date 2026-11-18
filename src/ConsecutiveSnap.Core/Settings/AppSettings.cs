using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.Core.Settings;

/// <summary>矩形をJSONで保存するための単純なデータ型（物理スクリーン座標）。</summary>
public sealed record RectDto(int X, int Y, int Width, int Height);

/// <summary>アプリの設定。起動時に復元し、開始時・終了時に保存する。</summary>
public sealed class AppSettings
{
    /// <summary>繰り返し回数の上限・下限。</summary>
    public const int MinRepeatCount = 1;
    public const int MaxRepeatCount = 9999;

    /// <summary>キャプチャ範囲（物理スクリーン座標）。未指定ならnull。</summary>
    public RectDto? CaptureRegion { get; set; }

    /// <summary>撮影の合間に上から順に実行するステップ。空でもよい。</summary>
    public List<ActionStep> Steps { get; set; } = new();

    /// <summary>繰り返し回数（撮影枚数）。</summary>
    public int RepeatCount { get; set; } = 10;

    /// <summary>開始ボタンから最初の撮影までの待機秒数。</summary>
    public int StartDelaySeconds { get; set; } = 5;

    /// <summary>ステップ実行後、次の撮影までの待機ミリ秒（ページ描画待ち）。</summary>
    public int AfterSequenceWaitMs { get; set; } = 500;

    /// <summary>クリックステップの後にマウスカーソルを元の位置へ戻すか。</summary>
    public bool RestoreCursorAfterClick { get; set; } = true;

    /// <summary>保存先の親フォルダ。</summary>
    public string ParentFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ConsecutiveSnap");

    /// <summary>作成するフォルダ名（親フォルダの直下に作る）。</summary>
    public string FolderName { get; set; } = "";
}
