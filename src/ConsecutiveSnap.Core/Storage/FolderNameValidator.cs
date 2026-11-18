namespace ConsecutiveSnap.Core.Storage;

/// <summary>
/// 保存フォルダ名（と、その親フォルダとの組み合わせ）がWindowsで安全に作成できるかを検証する。
/// </summary>
public static class FolderNameValidator
{
    // Windowsの予約デバイス名。拡張子付き（例: CON.txt）でも使えない。
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>MAX_PATH相当。これを超えるパスは作らない。</summary>
    private const int MaxPathLength = 259;

    // 別名フォルダの接尾辞（_9999）と、フォルダ内のファイル（\9999.png）の分の余裕。
    private const int SuffixReserve = 5;                 // "_9999"
    private const int FileReserve = 1 + 4 + 4;           // "\" + 4桁 + ".png"

    /// <summary>
    /// フォルダ名単体を検証する。問題なければnull、問題があれば利用者向けのエラーメッセージを返す。
    /// </summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "フォルダ名を入力してください。";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "フォルダ名に使えない文字が含まれています（\\ / : * ? \" < > | など）。";
        }

        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return "フォルダ名の末尾に「.」や空白は使えません。";
        }

        // 予約名は拡張子部分を除いた名前で判定する（"CON.txt" も不可）
        string baseName = name.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(baseName))
        {
            return $"「{baseName}」はWindowsの予約名のため使えません。";
        }

        return null;
    }

    /// <summary>
    /// 親フォルダとフォルダ名の組み合わせを検証する。
    /// フォルダ名の検証に加え、親フォルダの指定有無と、パス全体の長さ（別名接尾辞・ファイル名込み）を確認する。
    /// 問題なければnull、問題があれば利用者向けのエラーメッセージを返す。
    /// </summary>
    public static string? Validate(string? parentFolder, string? name)
    {
        if (string.IsNullOrWhiteSpace(parentFolder))
        {
            return "保存先フォルダを指定してください。";
        }

        if (parentFolder.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return "保存先フォルダに使えない文字が含まれています。";
        }

        string? nameError = ValidateName(name);
        if (nameError is not null)
        {
            return nameError;
        }

        int length = parentFolder.TrimEnd('\\', '/').Length + 1 + name!.Length + SuffixReserve + FileReserve;
        if (length > MaxPathLength)
        {
            return "保存先フォルダとフォルダ名の合計が長すぎます。短くしてください。";
        }

        return null;
    }
}
