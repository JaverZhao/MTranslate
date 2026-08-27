using MTranslate.Desktop.Services;
using MTranslate.Desktop.ViewModels;
using MTranslate.DocumentFormats;

namespace MTranslate.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Navigation_SelectsEachRegisteredPage()
    {
        var coordinator = new FakeCoordinator();
        var home = new HomeViewModel(coordinator, new FakeClipboard());
        var viewModel = new MainWindowViewModel(
            home,
            new FilesViewModel(coordinator, new FakeFilePicker()),
            new HistoryViewModel(),
            new ModelsViewModel(coordinator),
            new ApiViewModel(),
            new SettingsViewModel(coordinator));

        Assert.Same(home, viewModel.CurrentPage);
        foreach (var item in viewModel.Navigation)
        {
            item.SelectCommand.Execute(null);
            Assert.Same(item.Page, viewModel.CurrentPage);
            Assert.True(item.IsSelected);
            Assert.Single(viewModel.Navigation.Where(candidate => candidate.IsSelected));
        }
    }

    [Fact]
    public void Settings_UpdatesCoordinatorCachePreference()
    {
        var coordinator = new FakeCoordinator { CacheEnabled = true };
        var settings = new SettingsViewModel(coordinator);

        settings.CacheEnabled = false;

        Assert.False(coordinator.CacheEnabled);
    }

    private sealed class FakeCoordinator : ITranslationCoordinator
    {
        public bool CacheEnabled { get; set; }
        public string ModelStatus => "标准模型已安装";
        public Task<DesktopTranslationResponse> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DesktopTranslationResponse(text, TimeSpan.Zero, 0, 1));
        public Task<DocumentTranslationResult> TranslateDocumentAsync(string inputPath, string outputPath, string sourceLanguage, string targetLanguage, SubtitleOutputMode subtitleOutput, Guid jobId, IProgress<DocumentTranslationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public Task SetTextAsync(string text) => Task.CompletedTask;
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<IReadOnlyList<string>> PickDocumentsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickOutputFolderAsync() => Task.FromResult<string?>(null);
        public Task OpenContainingFolderAsync(string path) => Task.CompletedTask;
    }
}
