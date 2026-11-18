using System.Text.Json.Serialization;

namespace ConsecutiveSnap.Core.Steps;

/// <summary>キー入力に併用する修飾キー（組み合わせ可能なフラグ）。</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

/// <summary>
/// 撮影と撮影の間に実行する操作の1ステップ。
/// JSONへは "type" 判別子付きで保存し、読み込み時に元の型へ復元する。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyStep), "key")]
[JsonDerivedType(typeof(ClickStep), "click")]
[JsonDerivedType(typeof(WaitStep), "wait")]
[JsonDerivedType(typeof(WheelStep), "wheel")]
public abstract record ActionStep;

/// <summary>キー入力ステップ。VirtualKeyはWin32の仮想キーコード。</summary>
public sealed record KeyStep(int VirtualKey, KeyModifiers Modifiers) : ActionStep;

/// <summary>マウス左クリックステップ。座標は物理スクリーン座標（仮想スクリーン原点基準）。</summary>
public sealed record ClickStep(int X, int Y) : ActionStep;

/// <summary>待機ステップ。単位はミリ秒。</summary>
public sealed record WaitStep(int Milliseconds) : ActionStep;

/// <summary>
/// マウスホイールステップ。Notchesは回転量（ノッチ数）で、正が奥（上）方向、負が手前（下）方向。
/// X・Yを指定した場合は、その物理スクリーン座標へカーソルを動かしてから回す（ホイールはカーソル下のウィンドウに届くため）。
/// 両方nullなら現在のカーソル位置で回す。
/// </summary>
public sealed record WheelStep(int Notches, int? X = null, int? Y = null) : ActionStep
{
    /// <summary>1回のステップで回せるノッチ数の上限（絶対値）。</summary>
    public const int MaxNotches = 100;

    /// <summary>位置が指定されているか。</summary>
    [JsonIgnore]
    public bool HasPosition => X.HasValue && Y.HasValue;
}
