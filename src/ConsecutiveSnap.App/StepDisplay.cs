using System.Windows.Input;
using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.App;

/// <summary>ステップや修飾キーを、画面表示用の文字列にする。</summary>
internal static class StepDisplay
{
    /// <summary>仮想キーコードと修飾キーから、「Ctrl + →」のような表示名を作る。</summary>
    public static string FormatKey(int virtualKey, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(virtualKey));
        return string.Join(" + ", parts);
    }

    /// <summary>仮想キーコードから、人が読みやすいキー名を返す。</summary>
    private static string KeyName(int virtualKey)
    {
        Key key = KeyInterop.KeyFromVirtualKey(virtualKey);
        return key switch
        {
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Prior => "PageUp",
            Key.Next => "PageDown",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Capital => "CapsLock",
            Key.None => $"VK_0x{virtualKey:X2}",
            _ => key.ToString()
        };
    }

    /// <summary>ステップの種別名（一覧のバッジに表示する）。</summary>
    public static string Kind(ActionStep step) => step switch
    {
        KeyStep => "キー",
        ClickStep => "クリック",
        WheelStep => "ホイール",
        WaitStep => "待機",
        _ => step.GetType().Name
    };

    /// <summary>ステップの詳細（一覧のバッジの右に表示する）。</summary>
    public static string Detail(ActionStep step) => step switch
    {
        KeyStep k => FormatKey(k.VirtualKey, k.Modifiers),
        ClickStep c => $"({c.X}, {c.Y}) を左クリック",
        WheelStep w => DescribeWheel(w),
        WaitStep t => $"{t.Milliseconds} ミリ秒",
        _ => ""
    };

    /// <summary>ホイールステップを「下へ 3 ノッチ ／ 位置 (100, 200)」のような文字列にする。</summary>
    private static string DescribeWheel(WheelStep w)
    {
        string direction = w.Notches < 0 ? "下へ" : "上へ";
        string position = w.HasPosition ? $"  ／  位置 ({w.X}, {w.Y})" : "  ／  現在のカーソル位置";
        return $"{direction} {Math.Abs(w.Notches)} ノッチ{position}";
    }
}
