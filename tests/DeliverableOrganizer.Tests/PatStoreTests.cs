using DeliverableOrganizer.Services;
using DeliverableOrganizer.ViewModels;

namespace DeliverableOrganizer.Tests;

public class PatStoreTests
{
    private sealed class MemoryPatStore(string? initial = null) : IPatStore
    {
        public string? Saved { get; private set; } = initial;

        public string? Load() => Saved;

        public void Save(string pat) => Saved = pat;

        public void Delete() => Saved = null;
    }

    private sealed class NullDialogs : IDialogService
    {
        public string? PickFolder(string title, string? initialFolder) => null;

        public bool ShowPreview(PreviewViewModel viewModel) => false;

        public void ShowError(string message) { }

        public void OpenInExplorer(string folder) { }
    }

    [Fact]
    public void 保存済みのPATがあれば起動時に復元して保存するをオンにする()
    {
        var store = new MemoryPatStore("saved-pat");
        var vm = new MainViewModel(new NullDialogs(), store);

        Assert.Equal("saved-pat", vm.Pat);
        Assert.True(vm.SavePat);
        Assert.Equal("saved-pat", store.Saved);
    }

    [Fact]
    public void 保存済みのPATがなければ空でオフ()
    {
        var vm = new MainViewModel(new NullDialogs(), new MemoryPatStore());

        Assert.Equal("", vm.Pat);
        Assert.False(vm.SavePat);
    }

    [Fact]
    public void 保存するをオンにすると入力中のPATを保存する()
    {
        var store = new MemoryPatStore();
        var vm = new MainViewModel(new NullDialogs(), store) { Pat = " new-pat " };

        vm.SavePat = true;

        Assert.Equal("new-pat", store.Saved);
    }

    [Fact]
    public void 保存するをオフにすると保存済みのPATを消す()
    {
        var store = new MemoryPatStore("saved-pat");
        var vm = new MainViewModel(new NullDialogs(), store);

        vm.SavePat = false;

        Assert.Null(store.Saved);
    }

    [Fact]
    public void オンのままPATを変えると次の保存で上書きする()
    {
        var store = new MemoryPatStore("old-pat");
        var vm = new MainViewModel(new NullDialogs(), store) { Pat = "new-pat" };

        vm.PersistPat();

        Assert.Equal("new-pat", store.Saved);
    }

    [Fact]
    public void 資格情報マネージャーに保存して読み込み削除できる()
    {
        // 本物の資格情報マネージャーを使う。既存の PAT に触れないようテスト専用の名前にする
        var store = new WindowsCredentialPatStore($"DeliverableOrganizer:Test:{Guid.NewGuid():N}");
        try
        {
            Assert.Null(store.Load());

            store.Save("テスト-pat-123");
            Assert.Equal("テスト-pat-123", store.Load());

            store.Save("updated");
            Assert.Equal("updated", store.Load());
        }
        finally
        {
            store.Delete();
        }

        Assert.Null(store.Load());
        store.Delete(); // ないものを消してもエラーにしない
    }
}
