namespace ConsecutiveSnap.Core.Storage;

/// <summary>
/// 連番ファイル名の生成。桁数は「繰り返し回数の桁数」に合わせて0埋めする
/// （例: 50回なら01〜50、100回なら001〜100、9回なら1〜9）。
/// </summary>
public static class FileNameFormatter
{
    /// <summary>保存形式の拡張子（PNG固定）。</summary>
    public const string Extension = ".png";

    /// <summary>繰り返し回数から、連番の桁数を求める。</summary>
    /// <exception cref="ArgumentOutOfRangeException">回数が1未満の場合。</exception>
    public static int GetDigits(int repeatCount)
    {
        if (repeatCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatCount), "繰り返し回数は1以上である必要があります。");
        }
        return repeatCount.ToString().Length;
    }

    /// <summary>
    /// index番目（1始まり）のファイル名（拡張子付き）を返す。
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">indexが1〜repeatCountの範囲外の場合。</exception>
    public static string Format(int index, int repeatCount)
    {
        int digits = GetDigits(repeatCount);
        if (index < 1 || index > repeatCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"連番は1〜{repeatCount}の範囲である必要があります。");
        }
        return index.ToString($"D{digits}") + Extension;
    }
}
