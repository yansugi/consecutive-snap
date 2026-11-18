using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ConsecutiveSnap.App;

/// <summary>
/// 緊急停止キー（Pause/Break、単独押し）のグローバルホットキー。
/// SnipSnapのGlobalHotkeyServiceを元に、固定キー専用へ簡略化したもの。
/// 画面を持たないメッセージ専用ウィンドウでWM_HOTKEYを受け取るため、
/// 他のアプリにフォーカスがあっても反応する。UIスレッドで生成すること（コールバックもUIスレッドで呼ばれる）。
/// </summary>
internal sealed class EmergencyStopHotkey : NativeWindow, IDisposable
{
    private const uint ModNoRepeat = 0x4000;
    private const uint VkPause = 0x13;
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 1;

    /// <summary>メッセージ専用ウィンドウの親に指定する値（HWND_MESSAGE）。</summary>
    private static readonly IntPtr HwndMessage = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Action _onPressed;
    private bool _disposed;

    /// <summary>ホットキーを実際に登録できたか。falseなら他のアプリが同じキーを使っている等。</summary>
    public bool IsRegistered { get; }

    /// <summary>ホットキーを登録する。登録に失敗してもコンストラクターは例外を投げず、<see cref="IsRegistered"/>がfalseになる。</summary>
    public EmergencyStopHotkey(Action onPressed)
    {
        _onPressed = onPressed ?? throw new ArgumentNullException(nameof(onPressed));

        // 画面に表示されないメッセージ専用ウィンドウ（WM_HOTKEY受信専用）を作る
        CreateHandle(new CreateParams { Parent = HwndMessage });

        IsRegistered = RegisterHotKey(Handle, HotkeyId, ModNoRepeat, VkPause);
        if (!IsRegistered)
        {
            // 登録失敗時はウィンドウも不要なので破棄しておく
            DestroyHandle();
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            _onPressed();
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>ホットキーを解除してウィンドウを破棄する。複数回呼んでもよい。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (IsRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }
}
