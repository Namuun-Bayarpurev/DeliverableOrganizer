using DeliverableOrganizer.Services;

namespace DeliverableOrganizer.Tests;

public sealed class OutputFolderValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeliverableOrganizerTests", Guid.NewGuid().ToString("N"));

    public OutputFolderValidatorTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void 既存のフォルダは使える()
    {
        var check = OutputFolderValidator.Check(_root);

        Assert.Null(check.Error);
        Assert.False(check.Created);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root)); // 書き込み確認のファイルは消えている
    }

    [Fact]
    public void ないフォルダは作成する()
    {
        string folder = Path.Combine(_root, "新規", "納品");

        var check = OutputFolderValidator.Check(folder);

        Assert.Null(check.Error);
        Assert.True(check.Created);
        Assert.True(Directory.Exists(folder));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\folder")]
    [InlineData(@"C:folder")]
    [InlineData(@"C:\a\b*c")]
    [InlineData(@"C:\a\b?c")]
    [InlineData(@"C:\a\b<c")]
    [InlineData(@"C:\a\b|c")]
    public void 書式が正しくなければエラー(string folder)
    {
        var check = OutputFolderValidator.Check(folder);

        Assert.NotNull(check.Error);
        Assert.False(check.Created);
    }

    [Fact]
    public void ないドライブはエラー()
    {
        char? missing = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char?)c)
            .FirstOrDefault(c => !Directory.Exists($"{c}:\\"));
        Assert.NotNull(missing);

        var check = OutputFolderValidator.Check($@"{missing}:\out");

        Assert.Contains("ドライブが見つかりません", check.Error);
    }

    [Fact]
    public void ファイルを指定したらエラー()
    {
        string file = Path.Combine(_root, "a.txt");
        File.WriteAllText(file, "");

        Assert.Contains("フォルダを指定", OutputFolderValidator.Check(file).Error);
    }
}
