using System.IO;
using System.Text;

namespace ConsecutiveSnap.App;

/// <summary>
/// 実行ごとの簡易ログ（%APPDATA%\ConsecutiveSnap\logs\yyyyMMdd_HHmmss.log）。
/// ログ出力の失敗でアプリの動作を止めないよう、書き込みの例外は握りつぶす
/// （ログ以外の処理の結果を優先するため。失敗した事実は<see cref="LastError"/>で確認できる）。
/// </summary>
internal sealed class AppLog
{
    private readonly object _lock = new();

    /// <summary>このログファイルのフルパス。</summary>
    public string FilePath { get; }

    /// <summary>直近のログ書き込み失敗の内容（無ければnull）。</summary>
    public string? LastError { get; private set; }

    /// <summary>ログフォルダのパスを返す。</summary>
    public static string GetLogDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ConsecutiveSnap", "logs");
    }

    /// <summary>実行1回分のログファイルを生成する（ファイルは最初の書き込み時に作られる）。</summary>
    public AppLog()
    {
        FilePath = Path.Combine(GetLogDirectory(), $"{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    /// <summary>1行追記する。複数スレッドから呼ばれてもよい。</summary>
    public void Write(string message)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastError = ex.Message;
            }
        }
    }
}
