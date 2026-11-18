using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ConsecutiveSnap.App;

/// <summary>指定の種類。</summary>
internal enum PickMode
{
    /// <summary>ドラッグで矩形を選ぶ（撮影範囲の指定）。</summary>
    Region,
    /// <summary>クリックで1点を選ぶ（クリックステップの座標指定）。</summary>
    Point
}

/// <summary>
/// モニターごとにDPIが異なる環境でも正しく表示できるよう、指定オーバーレイはモニター1台につき1ウィンドウ
/// （<see cref="RegionPickerWindow"/>）を用意する。このクラスは、それらの間で共有する状態を持つ
/// （SnipSnapのCaptureSessionを元に、点指定モードと物理スクリーン座標での結果を追加したもの）。
/// ウィンドウ間で共有する座標は「キャプチャ画像のビットマップピクセル座標系」（画像の左上が(0,0)）で、
/// 結果だけを物理スクリーン座標へ変換して返す。
/// </summary>
internal sealed class PickSession
{
    public required PickMode Mode { get; init; }
    public required BitmapSource FullImageSource { get; init; }

    /// <summary>キャプチャ画像全体のビットマップピクセル範囲（(0,0)からWidth,Height）。</summary>
    public required DrawingRectangle FullBitmapBounds { get; init; }

    /// <summary>画像の左上に対応する物理スクリーン座標（仮想スクリーンの原点）。</summary>
    public required DrawingPoint Origin { get; init; }

    /// <summary>この回の指定に参加している全モニター分のオーバーレイウィンドウ。</summary>
    public List<RegionPickerWindow> Windows { get; } = new();

    public bool IsDragging { get; set; }
    public DrawingPoint DragStartBitmapPixel { get; set; }
    public DrawingRectangle CurrentSelectionBitmapPixel { get; set; } = DrawingRectangle.Empty;

    /// <summary>確定（true）／キャンセル（false）／未確定（null）。</summary>
    public bool? Confirmed { get; set; }

    /// <summary>Regionモード確定時の結果（物理スクリーン座標）。</summary>
    public DrawingRectangle? ResultRegion { get; set; }

    /// <summary>Pointモード確定時の結果（物理スクリーン座標）。</summary>
    public DrawingPoint? ResultPoint { get; set; }

    private bool _closing;

    /// <summary>
    /// いずれかのウィンドウで確定・キャンセルされた際に、全モニター分のウィンドウをまとめて閉じる。
    /// 複数のウィンドウから多重に呼ばれても一度だけ実行する。
    /// </summary>
    public void CloseAll()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;

        foreach (RegionPickerWindow window in Windows)
        {
            window.Close();
        }
    }
}
