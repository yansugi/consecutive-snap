using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.Core.Input;

/// <summary>
/// 仮想キーコードに関する知識（拡張キーの判定、緊急停止キー、表示名）をまとめたもの。
/// </summary>
public static class KeyCatalog
{
    /// <summary>緊急停止に使うキー（Pause/Break）の仮想キーコード。</summary>
    public const int PauseVirtualKey = 0x13;

    // 修飾キーの仮想キーコード
    public const int VkLShift = 0xA0;
    public const int VkLControl = 0xA2;
    public const int VkLMenu = 0xA4;
    public const int VkLWin = 0x5B;

    // 修飾キー単体として扱う仮想キーコード（本体キーとして登録できない）
    private static readonly HashSet<int> ModifierKeys = new()
    {
        0x10, 0x11, 0x12,       // Shift, Control, Menu(Alt)
        0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5,
        0x5B, 0x5C              // LWin, RWin
    };

    // SendInputで拡張キーフラグが必要なキー。付け忘れるとテンキー側の数字キーとして解釈される。
    private static readonly HashSet<int> ExtendedKeys = new()
    {
        0x21, 0x22, 0x23, 0x24,     // PageUp, PageDown, End, Home
        0x25, 0x26, 0x27, 0x28,     // ← ↑ → ↓
        0x2D, 0x2E,                 // Insert, Delete
        0x5B, 0x5C, 0x5D,           // LWin, RWin, Apps
        0xA3, 0xA5,                 // RControl, RMenu
        0x6F,                       // テンキーの /
        0x90,                       // NumLock
        0x2C                        // PrintScreen
    };

    /// <summary>指定の仮想キーに、拡張キーフラグを付ける必要があるか。</summary>
    public static bool IsExtendedKey(int virtualKey) => ExtendedKeys.Contains(virtualKey);

    /// <summary>修飾キー（Shift/Ctrl/Alt/Win）そのものか。</summary>
    public static bool IsModifierKey(int virtualKey) => ModifierKeys.Contains(virtualKey);

    /// <summary>緊急停止キー（Pause/Break）か。ステップとしての登録は拒否する。</summary>
    public static bool IsEmergencyStopKey(int virtualKey) => virtualKey == PauseVirtualKey;

    /// <summary>
    /// キーステップとして登録できるかを検証する。登録できなければ利用者向けの理由を返し、問題なければnull。
    /// </summary>
    public static string? ValidateKeyStep(int virtualKey)
    {
        if (virtualKey <= 0 || virtualKey > 0xFE)
        {
            return "このキーは登録できません。";
        }
        if (IsEmergencyStopKey(virtualKey))
        {
            return "Pause/Break は緊急停止に使うため、操作として登録できません。";
        }
        if (IsModifierKey(virtualKey))
        {
            return "Shift/Ctrl/Alt/Win 単独では登録できません。他のキーと組み合わせてください。";
        }
        return null;
    }

    /// <summary>修飾キーのフラグから、押下順（Ctrl→Alt→Shift→Win）の仮想キー一覧を返す。</summary>
    public static IReadOnlyList<int> GetModifierVirtualKeys(KeyModifiers modifiers)
    {
        var keys = new List<int>(4);
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) keys.Add(VkLControl);
        if (modifiers.HasFlag(KeyModifiers.Alt)) keys.Add(VkLMenu);
        if (modifiers.HasFlag(KeyModifiers.Shift)) keys.Add(VkLShift);
        if (modifiers.HasFlag(KeyModifiers.Win)) keys.Add(VkLWin);
        return keys;
    }
}
