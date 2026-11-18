using ConsecutiveSnap.Core.Storage;

namespace ConsecutiveSnap.Core.Tests;

/// <summary>連番ファイル名の桁数・書式のテスト。</summary>
public class FileNameFormatterTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(99, 2)]
    [InlineData(100, 3)]
    [InlineData(999, 3)]
    [InlineData(1000, 4)]
    [InlineData(9999, 4)]
    public void GetDigits_回数の桁数を返す(int repeatCount, int expected)
    {
        Assert.Equal(expected, FileNameFormatter.GetDigits(repeatCount));
    }

    [Theory]
    [InlineData(1, 9, "1.png")]
    [InlineData(9, 9, "9.png")]
    [InlineData(1, 50, "01.png")]
    [InlineData(50, 50, "50.png")]
    [InlineData(7, 100, "007.png")]
    [InlineData(100, 100, "100.png")]
    [InlineData(1, 1000, "0001.png")]
    [InlineData(9999, 9999, "9999.png")]
    public void Format_桁数に応じて0埋めする(int index, int repeatCount, string expected)
    {
        Assert.Equal(expected, FileNameFormatter.Format(index, repeatCount));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(11, 10)]
    [InlineData(-1, 10)]
    public void Format_範囲外の連番は例外(int index, int repeatCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FileNameFormatter.Format(index, repeatCount));
    }

    [Fact]
    public void GetDigits_0以下は例外()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FileNameFormatter.GetDigits(0));
    }
}

/// <summary>フォルダ名検証のテスト。</summary>
public class FolderNameValidatorTests
{
    [Theory]
    [InlineData("MyBook")]
    [InlineData("日本語フォルダ")]
    [InlineData("book 2026-01")]
    [InlineData("a.b")]
    public void ValidateName_正常な名前はnull(string name)
    {
        Assert.Null(FolderNameValidator.ValidateName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a|b")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL.txt")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void ValidateName_不正な名前はエラーメッセージ(string? name)
    {
        Assert.NotNull(FolderNameValidator.ValidateName(name));
    }

    [Fact]
    public void Validate_親フォルダ未指定はエラー()
    {
        Assert.NotNull(FolderNameValidator.Validate("", "MyBook"));
        Assert.NotNull(FolderNameValidator.Validate(null, "MyBook"));
    }

    [Fact]
    public void Validate_パスが長すぎるとエラー()
    {
        string longParent = @"C:\" + new string('a', 240);
        Assert.NotNull(FolderNameValidator.Validate(longParent, "MyBook"));
    }

    [Fact]
    public void Validate_通常のパスはnull()
    {
        Assert.Null(FolderNameValidator.Validate(@"C:\Users\test\Pictures", "MyBook"));
    }
}

/// <summary>保存フォルダ確保（別名自動作成）のテスト。</summary>
public class FolderAllocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ConsecutiveSnapTests_" + Guid.NewGuid().ToString("N"));

    public FolderAllocatorTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // 自分が作成した一時フォルダのみを後始末する
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Allocate_存在しなければそのままの名前で作る()
    {
        string path = FolderAllocator.Allocate(_root, "MyBook");

        Assert.Equal(Path.Combine(_root, "MyBook"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Allocate_親フォルダが無ければ作る()
    {
        string parent = Path.Combine(_root, "new_parent");

        string path = FolderAllocator.Allocate(parent, "MyBook");

        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Allocate_存在すれば_2を付ける()
    {
        Directory.CreateDirectory(Path.Combine(_root, "MyBook"));

        string path = FolderAllocator.Allocate(_root, "MyBook");

        Assert.Equal(Path.Combine(_root, "MyBook_2"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void Allocate_2も存在すれば_3を付ける()
    {
        Directory.CreateDirectory(Path.Combine(_root, "MyBook"));
        Directory.CreateDirectory(Path.Combine(_root, "MyBook_2"));

        string path = FolderAllocator.Allocate(_root, "MyBook");

        Assert.Equal(Path.Combine(_root, "MyBook_3"), path);
    }

    [Fact]
    public void Allocate_欠番があれば空いている最小の番号を使う()
    {
        Directory.CreateDirectory(Path.Combine(_root, "MyBook"));
        Directory.CreateDirectory(Path.Combine(_root, "MyBook_3"));

        string path = FolderAllocator.Allocate(_root, "MyBook");

        Assert.Equal(Path.Combine(_root, "MyBook_2"), path);
    }

    [Fact]
    public void Allocate_既存フォルダの中身は変わらない()
    {
        string existing = Path.Combine(_root, "MyBook");
        Directory.CreateDirectory(existing);
        string file = Path.Combine(existing, "01.png");
        File.WriteAllText(file, "original");

        FolderAllocator.Allocate(_root, "MyBook");

        Assert.Equal("original", File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(existing));
    }

    [Fact]
    public void Allocate_同名のファイルがあってもそれを避ける()
    {
        File.WriteAllText(Path.Combine(_root, "MyBook"), "file");

        string path = FolderAllocator.Allocate(_root, "MyBook");

        Assert.Equal(Path.Combine(_root, "MyBook_2"), path);
    }

    [Fact]
    public void Allocate_不正な名前は例外()
    {
        Assert.Throws<ArgumentException>(() => FolderAllocator.Allocate(_root, "a/b"));
    }
}
