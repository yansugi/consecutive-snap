namespace ConsecutiveSnap.Core.Storage;

/// <summary>
/// 保存先フォルダを確保する。同名フォルダが既に存在する場合は、既存フォルダには一切触れず、
/// 空いている最小の番号を付けた別名（name_2, name_3 ...）で新規作成する。
/// </summary>
public static class FolderAllocator
{
    /// <summary>別名の探索上限。これを超えても空きが無ければ異常として扱う。</summary>
    private const int MaxSuffix = 9999;

    /// <summary>
    /// 親フォルダの直下に保存用フォルダを新規作成し、そのフルパスを返す。
    /// 親フォルダが無ければ作成する。
    /// </summary>
    /// <exception cref="ArgumentException">親フォルダ・フォルダ名が不正な場合。</exception>
    /// <exception cref="IOException">別名の空きが無い、またはフォルダを作成できなかった場合。</exception>
    public static string Allocate(string parentFolder, string folderName)
    {
        string? error = FolderNameValidator.Validate(parentFolder, folderName);
        if (error is not null)
        {
            throw new ArgumentException(error);
        }

        try
        {
            Directory.CreateDirectory(parentFolder);

            string candidate = Path.Combine(parentFolder, folderName);
            int suffix = 2;
            while (Directory.Exists(candidate) || File.Exists(candidate))
            {
                if (suffix > MaxSuffix)
                {
                    throw new IOException($"「{folderName}」の別名フォルダを作成できませんでした（{MaxSuffix}個まで使用済み）。");
                }
                candidate = Path.Combine(parentFolder, $"{folderName}_{suffix}");
                suffix++;
            }

            Directory.CreateDirectory(candidate);
            return candidate;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PathTooLongException or DirectoryNotFoundException or NotSupportedException)
        {
            // 呼び出し側ではIOExceptionとして一括して扱えるよう、原因を内包して包み直す
            throw new IOException($"保存フォルダを作成できませんでした: {ex.Message}", ex);
        }
    }
}
