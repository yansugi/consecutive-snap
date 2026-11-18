using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ConsecutiveSnap.App;

/// <summary>
/// 範囲／座標指定オーバーレイ（SnipSnapのCaptureOverlayWindowを元に改造）。
/// モニター1台につき1つ生成し、そのモニターの物理範囲にぴったり重ねて表示する。
/// モニターごとにDPIが異なっていても、それぞれ自分の担当モニターの中だけで描画が完結するため正しく表示できる。
/// ドラッグ選択・確定・Escでのキャンセルは<see cref="PickSession"/>を介して全モニター分のウィンドウ間で共有する。
/// </summary>
public partial class RegionPickerWindow : Window
{
    private readonly PickSession _session;

    /// <summary>このウィンドウが担当するモニターの、キャプチャ画像内でのピクセル範囲。</summary>
    private readonly DrawingRectangle _monitorBitmapBounds;

    /// <summary>同モニターの物理スクリーン座標（Win32のデスクトップ座標系。SetWindowPosに使う）。</summary>
    private readonly DrawingRectangle _monitorScreenBounds;

    /// <inheritdoc cref="_monitorScreenBounds"/>
    internal DrawingRectangle MonitorScreenBounds => _monitorScreenBounds;

    // このウィンドウのDIP座標系（マウス座標等）と、キャプチャ画像の物理ピクセル座標系との倍率。
    // モニターごとに異なりうるため、SourceInitialized時点で実際のDPIから求め直す。
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;

    internal RegionPickerWindow(PickSession session, DrawingRectangle monitorBitmapBounds, DrawingRectangle monitorScreenBounds)
    {
        InitializeComponent();
        _session = session;
        _monitorBitmapBounds = monitorBitmapBounds;
        _monitorScreenBounds = monitorScreenBounds;

        HintText.Text = session.Mode == PickMode.Region
            ? "ドラッグで撮影範囲を選択（離すと確定）"
            : "クリックする位置を選択（クリックで確定）";

        // HWND作成時にWindowsが正しいモニターのDPIを検出できるよう、概算でよいのでDIP位置を先に置いておく。
        // 正確な位置・サイズはShowOnMonitorでSetWindowPosにより物理ピクセル単位で決め直す。
        Left = monitorScreenBounds.X;
        Top = monitorScreenBounds.Y;
        Width = monitorScreenBounds.Width;
        Height = monitorScreenBounds.Height;

        BackgroundImage.Source = new CroppedBitmap(
            session.FullImageSource,
            new Int32Rect(monitorBitmapBounds.X, monitorBitmapBounds.Y, monitorBitmapBounds.Width, monitorBitmapBounds.Height));

        SourceInitialized += (_, _) =>
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            _dpiScaleX = dpi.DpiScaleX;
            _dpiScaleY = dpi.DpiScaleY;
        };

        // 概算位置がDPIの異なる隣のモニターと重なって検出された場合の保険。
        // SetWindowPosで正しいモニターへ移動した時点でDPIが再検出され、ここが発火する。
        DpiChanged += (_, args) =>
        {
            _dpiScaleX = args.NewDpi.DpiScaleX;
            _dpiScaleY = args.NewDpi.DpiScaleY;
        };

        Loaded += (_, _) => ResetDimToFullScreen();
    }

    /// <summary>
    /// このウィンドウを、担当モニターの物理スクリーン座標にぴったり重なるよう配置して表示する。
    /// 表示自体は必ずWPF標準のShow()で行う（SetWindowPosだけで表示するとClosedイベントが正しく発火せず、
    /// 全ウィンドウのクローズ待ちが終わらずフリーズする不具合がSnipSnapであったため）。
    /// </summary>
    internal void ShowOnMonitor(bool activate)
    {
        ShowActivated = activate;
        Show();

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (!NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HwndTopmost,
                _monitorScreenBounds.X, _monitorScreenBounds.Y,
                _monitorScreenBounds.Width, _monitorScreenBounds.Height,
                NativeMethods.SwpNoActivate))
        {
            // 位置補正に失敗しても表示自体は継続できるため、例外にはせず概算位置のままにする。
            // （DPIが全モニター同一の環境では概算位置で正しく重なる）
            System.Diagnostics.Debug.WriteLine("オーバーレイの位置補正(SetWindowPos)に失敗しました。");
        }

        if (activate)
        {
            NativeMethods.SetForegroundWindow(hwnd);
            Activate();
            Keyboard.Focus(OverlayCanvas);
        }
    }

    /// <summary>選択開始前の状態として、暗い層で画面全体を覆う。</summary>
    private void ResetDimToFullScreen()
    {
        PlaceRect(DimTop, 0, 0, ActualWidth, ActualHeight);
        PlaceRect(DimBottom, 0, ActualHeight, 0, 0);
        PlaceRect(DimLeft, 0, 0, 0, 0);
        PlaceRect(DimRight, 0, 0, 0, 0);
        SelectionBorder.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
    }

    private static void PlaceRect(Rectangle rect, double left, double top, double width, double height)
    {
        Canvas.SetLeft(rect, left);
        Canvas.SetTop(rect, top);
        rect.Width = Math.Max(0, width);
        rect.Height = Math.Max(0, height);
    }

    /// <summary>このウィンドウのDIP座標（ローカル）を、キャプチャ画像のビットマップピクセル座標に変換する。</summary>
    private DrawingPoint ToBitmapPixel(Point localDip) => new(
        (int)Math.Round(_monitorBitmapBounds.X + localDip.X * _dpiScaleX),
        (int)Math.Round(_monitorBitmapBounds.Y + localDip.Y * _dpiScaleY));

    /// <summary>キャプチャ画像のビットマップピクセル座標を、このウィンドウのDIP座標（ローカル）に変換する。</summary>
    private Point ToLocalDip(DrawingPoint bitmapPixel) => new(
        (bitmapPixel.X - _monitorBitmapBounds.X) / _dpiScaleX,
        (bitmapPixel.Y - _monitorBitmapBounds.Y) / _dpiScaleY);

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DrawingPoint pixel = ToBitmapPixel(e.GetPosition(OverlayCanvas));

        if (_session.Mode == PickMode.Point)
        {
            ConfirmPoint(pixel);
            return;
        }

        _session.IsDragging = true;
        _session.DragStartBitmapPixel = pixel;
        OverlayCanvas.CaptureMouse();
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_session.Mode != PickMode.Region || !_session.IsDragging)
        {
            return;
        }

        // OSレベルでマウスをキャプチャしているため、カーソルが別モニターに出てもこのウィンドウが
        // イベントを受け取り続ける。変換式が線形なので、モニターの外に出た分も正しく変換できる。
        DrawingPoint current = ToBitmapPixel(e.GetPosition(OverlayCanvas));
        int x = Math.Min(_session.DragStartBitmapPixel.X, current.X);
        int y = Math.Min(_session.DragStartBitmapPixel.Y, current.Y);
        int width = Math.Abs(current.X - _session.DragStartBitmapPixel.X);
        int height = Math.Abs(current.Y - _session.DragStartBitmapPixel.Y);
        _session.CurrentSelectionBitmapPixel = new DrawingRectangle(x, y, width, height);

        // 選択範囲は全モニターにまたがりうるため、全ウィンドウの見た目を更新する。
        foreach (RegionPickerWindow window in _session.Windows)
        {
            window.UpdateSelectionVisuals();
        }
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_session.Mode != PickMode.Region)
        {
            return;
        }

        _session.IsDragging = false;
        OverlayCanvas.ReleaseMouseCapture();

        // マウスを離した時点で有効な選択範囲があれば、そのまま確定する
        ConfirmRegion();
    }

    /// <summary>
    /// 選択範囲（全モニター共通・ビットマップピクセル座標系）に応じて、このウィンドウ内での
    /// 暗い層・選択枠・サイズ表示ラベルを更新する。
    /// </summary>
    private void UpdateSelectionVisuals()
    {
        double canvasWidth = ActualWidth;
        double canvasHeight = ActualHeight;

        DrawingRectangle sel = _session.CurrentSelectionBitmapPixel;
        if (sel.Width < 1 || sel.Height < 1)
        {
            ResetDimToFullScreen();
            return;
        }

        Point topLeftDip = ToLocalDip(new DrawingPoint(sel.Left, sel.Top));
        Point bottomRightDip = ToLocalDip(new DrawingPoint(sel.Right, sel.Bottom));

        double left = Math.Clamp(topLeftDip.X, 0, canvasWidth);
        double top = Math.Clamp(topLeftDip.Y, 0, canvasHeight);
        double right = Math.Clamp(bottomRightDip.X, 0, canvasWidth);
        double bottom = Math.Clamp(bottomRightDip.Y, 0, canvasHeight);

        PlaceRect(DimTop, 0, 0, canvasWidth, top);
        PlaceRect(DimBottom, 0, bottom, canvasWidth, canvasHeight - bottom);
        PlaceRect(DimLeft, 0, top, left, bottom - top);
        PlaceRect(DimRight, right, top, canvasWidth - right, bottom - top);

        bool intersectsThisMonitor = right > left && bottom > top;
        if (!intersectsThisMonitor)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = right - left;
        SelectionBorder.Height = bottom - top;
        SelectionBorder.Visibility = Visibility.Visible;

        // サイズ表示は、選択範囲の起点（ドラッグを始めた側）を含むモニターにだけ出す（重複表示を避けるため）。
        if (_monitorBitmapBounds.Contains(sel.Left, sel.Top))
        {
            SizeText.Text = $"{sel.Width} × {sel.Height}";
            Canvas.SetLeft(SizeBadge, left);
            Canvas.SetTop(SizeBadge, Math.Max(0, top - 28));
            SizeBadge.Visibility = Visibility.Visible;
        }
        else
        {
            SizeBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _session.Confirmed = false;
            _session.CloseAll();
        }
    }

    /// <summary>
    /// Regionモード: 選択範囲を物理スクリーン座標に変換して結果に格納し、全ウィンドウを閉じる。
    /// 選択がまだ行われていない場合は何もしない。
    /// </summary>
    private void ConfirmRegion()
    {
        DrawingRectangle sel = _session.CurrentSelectionBitmapPixel;
        if (sel.Width < 1 || sel.Height < 1)
        {
            return;
        }

        // 丸め誤差でビットマップ範囲をわずかに超えることがあるため、範囲内に収める。
        DrawingRectangle crop = DrawingRectangle.Intersect(sel, _session.FullBitmapBounds);
        if (crop.Width < 1 || crop.Height < 1)
        {
            return;
        }

        _session.ResultRegion = new DrawingRectangle(
            crop.X + _session.Origin.X, crop.Y + _session.Origin.Y, crop.Width, crop.Height);
        _session.Confirmed = true;
        _session.CloseAll();
    }

    /// <summary>Pointモード: クリックした位置を物理スクリーン座標に変換して結果に格納し、全ウィンドウを閉じる。</summary>
    private void ConfirmPoint(DrawingPoint bitmapPixel)
    {
        if (!_session.FullBitmapBounds.Contains(bitmapPixel))
        {
            return;
        }

        _session.ResultPoint = new DrawingPoint(
            bitmapPixel.X + _session.Origin.X, bitmapPixel.Y + _session.Origin.Y);
        _session.Confirmed = true;
        _session.CloseAll();
    }
}
