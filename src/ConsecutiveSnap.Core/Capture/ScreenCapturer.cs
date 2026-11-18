using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ConsecutiveSnap.Core.Capture;

/// <summary>画面を撮影して<see cref="Bitmap"/>を返すサービス。テスト時に差し替えられるようインターフェース化している。</summary>
public interface IScreenCapturer
{
    /// <summary>物理スクリーン座標の矩形を撮影する。返したBitmapの破棄は呼び出し側の責任。</summary>
    Bitmap Capture(Rectangle screenRect);
}

/// <summary>仮想スクリーン全体の撮影結果。原点（左上の物理スクリーン座標）を併せて保持する。</summary>
public sealed class CapturedImage : IDisposable
{
    /// <summary>撮影した画像。</summary>
    public Bitmap Bitmap { get; }

    /// <summary>画像の左上が対応する物理スクリーン座標X。</summary>
    public int OriginX { get; }

    /// <summary>画像の左上が対応する物理スクリーン座標Y。</summary>
    public int OriginY { get; }

    public CapturedImage(Bitmap bitmap, int originX, int originY)
    {
        Bitmap = bitmap;
        OriginX = originX;
        OriginY = originY;
    }

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>
/// GDI（Graphics.CopyFromScreen）による画面キャプチャの実装（SnipSnapのScreenCaptureServiceを元に、
/// 指定矩形の撮影を追加したもの）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenCapturer : IScreenCapturer
{
    // GetSystemMetricsに渡すインデックス。仮想スクリーン（全モニターを合わせた領域）の原点とサイズを取得する。
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>仮想スクリーン全体（全モニターを合わせた領域）の物理スクリーン座標での範囲を返す。</summary>
    public static Rectangle GetVirtualScreenBounds()
    {
        return new Rectangle(
            GetSystemMetrics(SM_XVIRTUALSCREEN),
            GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN),
            GetSystemMetrics(SM_CYVIRTUALSCREEN));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">矩形のサイズが不正な場合。</exception>
    /// <exception cref="InvalidOperationException">撮影に失敗した場合（原因を内包）。</exception>
    public Bitmap Capture(Rectangle screenRect)
    {
        if (screenRect.Width < 1 || screenRect.Height < 1)
        {
            throw new ArgumentException("撮影範囲のサイズが不正です。", nameof(screenRect));
        }

        var bitmap = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(screenRect.X, screenRect.Y, 0, 0, screenRect.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch (Exception ex)
        {
            // 失敗時は確保したビットマップを解放してから、原因を内包した例外にして伝播する
            bitmap.Dispose();
            throw new InvalidOperationException($"画面の撮影に失敗しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 仮想スクリーン全体を撮影する（範囲指定オーバーレイの背景用）。
    /// </summary>
    /// <exception cref="InvalidOperationException">サイズを取得できない、または撮影に失敗した場合。</exception>
    public CapturedImage CaptureVirtualScreen()
    {
        Rectangle bounds = GetVirtualScreenBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("仮想スクリーンのサイズを取得できませんでした。");
        }

        Bitmap bitmap = Capture(bounds);
        return new CapturedImage(bitmap, bounds.X, bounds.Y);
    }
}
