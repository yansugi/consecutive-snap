using System.Windows;
using System.Windows.Interop;
using ConsecutiveSnap.Core.Runner;
using DrawingRectangle = System.Drawing.Rectangle;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace ConsecutiveSnap.App;

/// <summary>
/// 実行中に表示する小さな進捗ウィンドウ。撮影範囲に写り込まないことが最優先のため、
/// (1) 撮影範囲と重ならない位置に置く、(2) 画面キャプチャ対象から除外する（WDA_EXCLUDEFROMCAPTURE）、
/// の二重の対策を取る。さらに、対象アプリのフォーカスを奪わないスタイル（NOACTIVATE）と、
/// クリックを透過するスタイルを設定する。
/// </summary>
public partial class ProgressWindow : Window
{
    /// <summary>画面端からの余白（物理ピクセル）。</summary>
    private const int EdgeMargin = 12;

    public ProgressWindow()
    {
        InitializeComponent();
        MainText.Text = "準備中...";
        SubText.Text = "緊急停止: Pause/Break";

        SourceInitialized += (_, _) => ApplyWindowStyles();
    }

    /// <summary>フォーカスを奪わない・クリック透過・Alt+Tabに出さない・キャプチャ除外の設定を行う。</summary>
    private void ApplyWindowStyles()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.AddExStyle(hwnd,
            NativeMethods.WsExNoActivate | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow);

        // Windows 10 2004より前のOSでは失敗する。その場合は配置による回避のみで対処する（例外にはしない）。
        if (!NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture))
        {
            System.Diagnostics.Debug.WriteLine("SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)に失敗しました。配置のみで対処します。");
        }
    }

    /// <summary>進捗に応じて表示内容を更新する。</summary>
    public void UpdateProgress(RunProgress progress)
    {
        if (progress.Phase == RunPhase.Countdown)
        {
            MainText.Text = $"開始まで {progress.CountdownRemaining} 秒";
            SubText.Text = "対象アプリにフォーカスを合わせてください  /  緊急停止: Pause/Break";
        }
        else
        {
            MainText.Text = $"撮影中 {progress.Current} / {progress.Total}";
            SubText.Text = "緊急停止: Pause/Break";
        }
    }

    /// <summary>
    /// 撮影範囲と重ならない位置（各モニターの四隅）を探して配置し、表示する。
    /// 重ならない位置が見つからない場合（範囲が画面全体に近い場合など）は、写り込みを避けるため表示しない。
    /// </summary>
    /// <returns>表示できたらtrue。</returns>
    public bool ShowOutside(DrawingRectangle captureRegion)
    {
        ShowActivated = false;
        Show();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;

        // 撮影範囲に少し余裕を持たせた領域を、避けるべき領域とする
        DrawingRectangle avoid = captureRegion;
        avoid.Inflate(EdgeMargin, EdgeMargin);

        foreach (WinFormsScreen screen in WinFormsScreen.AllScreens)
        {
            DrawingRectangle area = screen.WorkingArea;

            // 各四隅を試す。移動するとモニターのDPIが変わりサイズが変わりうるため、移動後に実サイズを取り直して判定する。
            for (int corner = 0; corner < 4; corner++)
            {
                if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect before))
                {
                    continue;
                }
                int width = before.Right - before.Left;
                int height = before.Bottom - before.Top;

                int x = (corner % 2 == 0) ? area.Left + EdgeMargin : area.Right - width - EdgeMargin;
                int y = (corner < 2) ? area.Top + EdgeMargin : area.Bottom - height - EdgeMargin;

                if (!NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, x, y, 0, 0,
                        NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate))
                {
                    continue;
                }
                if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.Rect after))
                {
                    continue;
                }

                var placed = DrawingRectangle.FromLTRB(after.Left, after.Top, after.Right, after.Bottom);
                if (area.Contains(placed) && !placed.IntersectsWith(avoid))
                {
                    return true;
                }
            }
        }

        // 安全な位置が無かったため、写り込みを避けて表示しない
        Hide();
        return false;
    }
}
