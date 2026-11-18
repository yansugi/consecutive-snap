using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConsecutiveSnap.Core.Capture;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using WinFormsCursor = System.Windows.Forms.Cursor;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace ConsecutiveSnap.App;

/// <summary>
/// 範囲・座標指定の実行役。仮想スクリーン全体の静止画を撮り、モニターごとのオーバーレイを表示して、
/// 選択が確定・キャンセルされるまで待つ（SnipSnapのApp.xaml.csの処理を元にしたもの）。
/// 呼び出し前に、自分のウィンドウが写り込まないよう最小化・非表示にしておくこと。UIスレッドから呼ぶ。
/// </summary>
internal static class RegionPicker
{
    /// <summary>撮影範囲をドラッグで指定する。キャンセル時はnull。物理スクリーン座標で返す。</summary>
    /// <exception cref="InvalidOperationException">画面の撮影に失敗した場合。</exception>
    public static DrawingRectangle? PickRegion()
    {
        PickSession session = RunPick(PickMode.Region);
        return session.Confirmed == true ? session.ResultRegion : null;
    }

    /// <summary>クリック位置を1点指定する。キャンセル時はnull。物理スクリーン座標で返す。</summary>
    /// <exception cref="InvalidOperationException">画面の撮影に失敗した場合。</exception>
    public static DrawingPoint? PickPoint()
    {
        PickSession session = RunPick(PickMode.Point);
        return session.Confirmed == true ? session.ResultPoint : null;
    }

    /// <summary>オーバーレイを表示して、全て閉じるまで待ち、セッションを返す。</summary>
    private static PickSession RunPick(PickMode mode)
    {
        using CapturedImage captured = new ScreenCapturer().CaptureVirtualScreen();
        PickSession session = CreateSession(mode, captured);

        var frame = new DispatcherFrame();
        int remaining = session.Windows.Count;

        foreach (RegionPickerWindow window in session.Windows)
        {
            window.Closed += (_, _) =>
            {
                remaining--;
                if (remaining <= 0)
                {
                    frame.Continue = false;
                }
            };
        }

        // カーソルが乗っているモニターのウィンドウをアクティブにし、Escキーがすぐ効くようにする。
        // どのモニターとも一致しない場合に備えて、必ず1つはアクティブにする。
        DrawingPoint cursor = WinFormsCursor.Position;
        RegionPickerWindow toActivate =
            session.Windows.FirstOrDefault(w => w.MonitorScreenBounds.Contains(cursor))
            ?? session.Windows[0];

        foreach (RegionPickerWindow window in session.Windows)
        {
            window.ShowOnMonitor(ReferenceEquals(window, toActivate));
        }

        // ShowDialogは1ウィンドウしか対象にできないため、DispatcherFrameで同等のブロッキングを行う。
        Dispatcher.PushFrame(frame);
        return session;
    }

    /// <summary>撮影した仮想スクリーン全体から、モニターごとのオーバーレイ一式を作る。</summary>
    private static PickSession CreateSession(PickMode mode, CapturedImage captured)
    {
        var session = new PickSession
        {
            Mode = mode,
            FullImageSource = ConvertToBitmapSource(captured.Bitmap),
            FullBitmapBounds = new DrawingRectangle(0, 0, captured.Bitmap.Width, captured.Bitmap.Height),
            Origin = new DrawingPoint(captured.OriginX, captured.OriginY)
        };

        foreach (WinFormsScreen screen in WinFormsScreen.AllScreens)
        {
            // screen.Boundsは物理スクリーン座標。キャプチャ画像内でのピクセル範囲にするため、原点ぶんを引く。
            var monitorBitmapBounds = new DrawingRectangle(
                screen.Bounds.X - captured.OriginX,
                screen.Bounds.Y - captured.OriginY,
                screen.Bounds.Width,
                screen.Bounds.Height);

            session.Windows.Add(new RegionPickerWindow(session, monitorBitmapBounds, screen.Bounds));
        }

        return session;
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// System.Drawing.Bitmap を WPF で表示できる BitmapSource に変換する。
    /// GDIハンドルはリークするとリソースを消費し続けるため、変換後に必ず解放する。
    /// </summary>
    private static BitmapSource ConvertToBitmapSource(DrawingBitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }
}
