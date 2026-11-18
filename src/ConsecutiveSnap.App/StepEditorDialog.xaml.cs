using System.Windows;
using System.Windows.Input;
using ConsecutiveSnap.Core.Input;
using ConsecutiveSnap.Core.Steps;

namespace ConsecutiveSnap.App;

/// <summary>
/// 操作ステップ（キー入力／マウスクリック／待機）を1件追加・編集するダイアログ。
/// OKで確定した結果は<see cref="Result"/>に入る。
/// </summary>
public partial class StepEditorDialog : Window
{
    /// <summary>クリック位置を画面上で指定する処理（メイン側が、ウィンドウの最小化等を含めて実行する）。</summary>
    private readonly Func<Window, Task<System.Drawing.Point?>> _pickPoint;

    // キー入力欄で取得済みのキー
    private int _capturedVirtualKey;
    private KeyModifiers _capturedModifiers;

    /// <summary>OKで確定したステップ。キャンセル時はnull。</summary>
    public ActionStep? Result { get; private set; }

    /// <summary>新規追加または編集用に生成する。existingがあればその内容を初期表示する。</summary>
    internal StepEditorDialog(ActionStep? existing, Func<Window, Task<System.Drawing.Point?>> pickPoint)
    {
        InitializeComponent();
        _pickPoint = pickPoint;
        ClickXBox.Text = "0";
        ClickYBox.Text = "0";
        WaitBox.Text = "500";
        WheelNotchBox.Text = "3";
        WheelXBox.Text = "0";
        WheelYBox.Text = "0";

        switch (existing)
        {
            case WheelStep wheel:
                WheelRadio.IsChecked = true;
                WheelNotchBox.Text = Math.Abs(wheel.Notches).ToString();
                WheelUpRadio.IsChecked = wheel.Notches > 0;
                WheelDownRadio.IsChecked = wheel.Notches <= 0;
                if (wheel.HasPosition)
                {
                    WheelPositionCheck.IsChecked = true;
                    WheelXBox.Text = wheel.X.ToString();
                    WheelYBox.Text = wheel.Y.ToString();
                }
                break;
            case KeyStep k:
                KeyRadio.IsChecked = true;
                _capturedVirtualKey = k.VirtualKey;
                _capturedModifiers = k.Modifiers;
                KeyBox.Text = StepDisplay.FormatKey(k.VirtualKey, k.Modifiers);
                break;
            case ClickStep c:
                ClickRadio.IsChecked = true;
                ClickXBox.Text = c.X.ToString();
                ClickYBox.Text = c.Y.ToString();
                break;
            case WaitStep w:
                WaitRadio.IsChecked = true;
                WaitBox.Text = w.Milliseconds.ToString();
                break;
        }
    }

    /// <summary>種類の選択に応じて、入力パネルを切り替える。</summary>
    private void TypeChanged(object sender, RoutedEventArgs e)
    {
        // InitializeComponent中（パネル生成前）にCheckedが発火した場合は何もしない
        if (KeyPanel is null || ClickPanel is null || WheelPanel is null || WaitPanel is null)
        {
            return;
        }

        KeyPanel.Visibility = KeyRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ClickPanel.Visibility = ClickRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WheelPanel.Visibility = WheelRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        WaitPanel.Visibility = WaitRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Text = "";
    }

    /// <summary>ホイールの「位置を指定する」チェックに応じて、座標入力欄の表示を切り替える。</summary>
    private void WheelPositionChanged(object sender, RoutedEventArgs e)
    {
        // InitializeComponent中（パネル生成前）に発火した場合は何もしない
        if (WheelPositionPanel is null)
        {
            return;
        }
        WheelPositionPanel.Visibility = WheelPositionCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>キー入力欄で押されたキーを取得する。修飾キー単独の押下は、本体キーが押されるまで確定しない。</summary>
    private void KeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt併用時はSystemKey、IME有効時はImeProcessedKeyに本来のキーが入る
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key
        };

        if (key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        string? error = KeyCatalog.ValidateKeyStep(vk);
        if (error is not null)
        {
            ErrorText.Text = error;
            return;
        }

        KeyModifiers modifiers = KeyModifiers.None;
        ModifierKeys current = Keyboard.Modifiers;
        if (current.HasFlag(ModifierKeys.Control)) modifiers |= KeyModifiers.Ctrl;
        if (current.HasFlag(ModifierKeys.Alt)) modifiers |= KeyModifiers.Alt;
        if (current.HasFlag(ModifierKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (current.HasFlag(ModifierKeys.Windows)) modifiers |= KeyModifiers.Win;

        _capturedVirtualKey = vk;
        _capturedModifiers = modifiers;
        KeyBox.Text = StepDisplay.FormatKey(vk, modifiers);
        ErrorText.Text = "";
    }

    /// <summary>クリック位置を画面上で指定する。</summary>
    private async void PickClickPointButton_Click(object sender, RoutedEventArgs e) =>
        await PickPointIntoAsync(ClickXBox, ClickYBox);

    /// <summary>ホイールを回す位置を画面上で指定する。</summary>
    private async void PickWheelPointButton_Click(object sender, RoutedEventArgs e) =>
        await PickPointIntoAsync(WheelXBox, WheelYBox);

    /// <summary>画面上で1点を指定してもらい、結果を指定のX・Y入力欄へ入れる。</summary>
    private async Task PickPointIntoAsync(System.Windows.Controls.TextBox xBox, System.Windows.Controls.TextBox yBox)
    {
        try
        {
            System.Drawing.Point? picked = await _pickPoint(this);
            if (picked is { } p)
            {
                xBox.Text = p.X.ToString();
                yBox.Text = p.Y.ToString();
                ErrorText.Text = "";
            }
        }
        catch (Exception ex)
        {
            // async voidから呼ばれるため、例外はここで必ず受け止めて利用者に伝える
            ErrorText.Text = $"位置の指定に失敗しました: {ex.Message}";
        }
    }

    /// <summary>入力を検証してステップを確定する。</summary>
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (KeyRadio.IsChecked == true)
        {
            if (_capturedVirtualKey == 0)
            {
                ErrorText.Text = "キーを押して指定してください。";
                return;
            }
            Result = new KeyStep(_capturedVirtualKey, _capturedModifiers);
        }
        else if (ClickRadio.IsChecked == true)
        {
            if (!int.TryParse(ClickXBox.Text, out int x) || !int.TryParse(ClickYBox.Text, out int y))
            {
                ErrorText.Text = "X・Yには整数を入力してください。";
                return;
            }
            Result = new ClickStep(x, y);
        }
        else if (WheelRadio.IsChecked == true)
        {
            if (!int.TryParse(WheelNotchBox.Text, out int notches) || notches < 1 || notches > WheelStep.MaxNotches)
            {
                ErrorText.Text = $"ホイールの量は1〜{WheelStep.MaxNotches}の整数で入力してください。";
                return;
            }

            int? x = null;
            int? y = null;
            if (WheelPositionCheck.IsChecked == true)
            {
                if (!int.TryParse(WheelXBox.Text, out int px) || !int.TryParse(WheelYBox.Text, out int py))
                {
                    ErrorText.Text = "X・Yには整数を入力してください。";
                    return;
                }
                x = px;
                y = py;
            }

            // 下方向は負の値で表す
            int signed = WheelDownRadio.IsChecked == true ? -notches : notches;
            Result = new WheelStep(signed, x, y);
        }
        else
        {
            if (!int.TryParse(WaitBox.Text, out int ms) || ms < 0 || ms > 600000)
            {
                ErrorText.Text = "待機時間は0〜600000の整数で入力してください。";
                return;
            }
            Result = new WaitStep(ms);
        }

        DialogResult = true;
    }
}
