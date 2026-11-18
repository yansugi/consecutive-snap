using System.Runtime.InteropServices;

namespace ConsecutiveSnap.App;

/// <summary>アプリ全体で使うWin32 APIの宣言。</summary>
internal static class NativeMethods
{
    // SetWindowPosのフラグ
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoActivate = 0x0010;
    public static readonly IntPtr HwndTopmost = new(-1);

    // 拡張ウィンドウスタイル
    public const int GwlExStyle = -20;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;

    // SetWindowDisplayAffinity: 画面キャプチャから除外する（Windows 10 2004以降）
    public const uint WdaExcludeFromCapture = 0x00000011;

    // ShowWindow
    public const int SwRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    /// <summary>拡張ウィンドウスタイルにフラグを追加する。</summary>
    public static void AddExStyle(IntPtr hwnd, int flags)
    {
        long current = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(current | (uint)flags));
    }
}
