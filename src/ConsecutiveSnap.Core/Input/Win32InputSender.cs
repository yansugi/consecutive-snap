using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.Core.Input;

/// <summary>キー入力・マウスクリックを他のアプリへ送るサービス。テスト時に差し替えられるようインターフェース化している。</summary>
public interface IInputSender
{
    /// <summary>修飾キー付きのキーを1回押して離す。</summary>
    void SendKey(int virtualKey, KeyModifiers modifiers);

    /// <summary>指定の物理スクリーン座標を左クリックする。restoreCursorがtrueなら元のカーソル位置へ戻す。</summary>
    void ClickAt(int x, int y, bool restoreCursor);

    /// <summary>
    /// マウスホイールを回す。notchesは正が奥（上）、負が手前（下）。
    /// x・yが両方指定されていれば、そこへカーソルを動かしてから回す（restoreCursorなら元へ戻す）。
    /// </summary>
    void Wheel(int notches, int? x, int? y, bool restoreCursor);

    /// <summary>修飾キーを全て解放する（中断・異常時に押しっぱなしになるのを防ぐための後始末）。</summary>
    void ReleaseAllKeys();
}

/// <summary>
/// SendInput / SetCursorPos による入力送信の実装。
/// 失敗時は握りつぶさず<see cref="Win32Exception"/>を投げる（昇格アプリへの送信がUIPIで拒否された場合も検知できる）。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32InputSender : IInputSender
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    /// <summary>ホイール1ノッチ分の値（WHEEL_DELTA）。</summary>
    private const int WheelDelta = 120;

    private const uint MAPVK_VK_TO_VSC = 0;

    /// <summary>クリック後にカーソルを戻す前の待機（戻すのが早すぎるとクリックが取りこぼされるアプリがある）。</summary>
    private const int RestoreCursorDelayMs = 50;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <inheritdoc />
    public void SendKey(int virtualKey, KeyModifiers modifiers)
    {
        string? error = KeyCatalog.ValidateKeyStep(virtualKey);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(virtualKey));
        }

        IReadOnlyList<int> modifierKeys = KeyCatalog.GetModifierVirtualKeys(modifiers);

        // 「修飾キー押下 → 本体キー押下・解放 → 修飾キー解放（逆順）」を1回のSendInputでまとめて送る。
        // 1回で送ることで、途中に他の入力が割り込んだり、中断で修飾キーが押しっぱなしになったりしない。
        var inputs = new List<INPUT>(modifierKeys.Count * 2 + 2);
        foreach (int mod in modifierKeys)
        {
            inputs.Add(CreateKeyInput(mod, keyUp: false));
        }
        inputs.Add(CreateKeyInput(virtualKey, keyUp: false));
        inputs.Add(CreateKeyInput(virtualKey, keyUp: true));
        for (int i = modifierKeys.Count - 1; i >= 0; i--)
        {
            inputs.Add(CreateKeyInput(modifierKeys[i], keyUp: true));
        }

        uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count)
        {
            int lastError = Marshal.GetLastWin32Error();
            // 一部だけ送られた場合に備えて、押した可能性のあるキーを解放してから失敗を通知する
            TryReleaseKeys(modifierKeys, virtualKey);
            throw new Win32Exception(lastError,
                $"キー入力を送信できませんでした（{sent}/{inputs.Count}件）。対象アプリが管理者権限で動いている場合、入力は届きません。");
        }
    }

    /// <inheritdoc />
    public void ClickAt(int x, int y, bool restoreCursor)
    {
        POINT original = default;
        if (restoreCursor && !GetCursorPos(out original))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "マウスカーソルの現在位置を取得できませんでした。");
        }

        if (!SetCursorPos(x, y))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"マウスカーソルを({x}, {y})へ移動できませんでした。");
        }

        INPUT[] clicks =
        {
            CreateMouseInput(MOUSEEVENTF_LEFTDOWN),
            CreateMouseInput(MOUSEEVENTF_LEFTUP)
        };

        uint sent = SendInput((uint)clicks.Length, clicks, Marshal.SizeOf<INPUT>());
        if (sent != clicks.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"クリックを送信できませんでした（{sent}/{clicks.Length}件）。対象アプリが管理者権限で動いている場合、入力は届きません。");
        }

        if (restoreCursor)
        {
            Thread.Sleep(RestoreCursorDelayMs);
            if (!SetCursorPos(original.X, original.Y))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "マウスカーソルを元の位置へ戻せませんでした。");
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">ノッチ数が0、または上限を超える場合。</exception>
    public void Wheel(int notches, int? x, int? y, bool restoreCursor)
    {
        if (notches == 0 || Math.Abs(notches) > WheelStep.MaxNotches)
        {
            throw new ArgumentOutOfRangeException(nameof(notches), $"ホイールのノッチ数は±1〜±{WheelStep.MaxNotches}の範囲で指定してください。");
        }

        bool move = x.HasValue && y.HasValue;
        POINT original = default;
        if (move && restoreCursor && !GetCursorPos(out original))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "マウスカーソルの現在位置を取得できませんでした。");
        }

        if (move && !SetCursorPos(x!.Value, y!.Value))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"マウスカーソルを({x}, {y})へ移動できませんでした。");
        }

        // mouseDataは符号付きの回転量（負＝手前）をuintのビット列として渡す
        INPUT[] wheel =
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0, dy = 0,
                        mouseData = unchecked((uint)(notches * WheelDelta)),
                        dwFlags = MOUSEEVENTF_WHEEL, time = 0, dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        };

        uint sent = SendInput((uint)wheel.Length, wheel, Marshal.SizeOf<INPUT>());
        if (sent != wheel.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "ホイール操作を送信できませんでした。対象アプリが管理者権限で動いている場合、入力は届きません。");
        }

        if (move && restoreCursor)
        {
            Thread.Sleep(RestoreCursorDelayMs);
            if (!SetCursorPos(original.X, original.Y))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "マウスカーソルを元の位置へ戻せませんでした。");
            }
        }
    }

    /// <inheritdoc />
    public void ReleaseAllKeys()
    {
        TryReleaseKeys(new[] { KeyCatalog.VkLControl, KeyCatalog.VkLMenu, KeyCatalog.VkLShift, KeyCatalog.VkLWin }, null);
    }

    /// <summary>
    /// 指定のキーを解放する入力を送る。後始末用のため、送信に失敗しても例外は投げない
    /// （元の例外を隠さないため）。
    /// </summary>
    private static void TryReleaseKeys(IReadOnlyList<int> modifierKeys, int? mainKey)
    {
        try
        {
            var inputs = new List<INPUT>();
            if (mainKey is int key)
            {
                inputs.Add(CreateKeyInput(key, keyUp: true));
            }
            foreach (int mod in modifierKeys)
            {
                inputs.Add(CreateKeyInput(mod, keyUp: true));
            }
            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            }
        }
        catch (Exception)
        {
            // 後始末の失敗は握りつぶす（呼び出し元で既に別の失敗を報告しているため）
        }
    }

    /// <summary>キーボード入力イベントを組み立てる。拡張キーにはフラグを付ける。</summary>
    private static INPUT CreateKeyInput(int virtualKey, bool keyUp)
    {
        uint flags = 0;
        if (KeyCatalog.IsExtendedKey(virtualKey))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }
        if (keyUp)
        {
            flags |= KEYEVENTF_KEYUP;
        }

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    wScan = (ushort)MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    /// <summary>マウスボタン入力イベント（現在のカーソル位置に対して）を組み立てる。</summary>
    private static INPUT CreateMouseInput(uint flags)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
            }
        };
    }
}
