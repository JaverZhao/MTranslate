using MTranslate.Desktop.Services;
using MTranslate.Desktop.ViewModels;
using MTranslate.DocumentFormats;
using MTranslate.Api;
using FluentIcons.Common;

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
            new HistoryViewModel(coordinator, new FakeClipboard()),
            new ModelsViewModel(coordinator),
            new ApiViewModel(new FakeLocalApiService()),
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
        settings.HistoryEnabled = false;
        settings.Acceleration = "GPU";

        Assert.False(coordinator.CacheEnabled);
        Assert.False(coordinator.HistoryEnabled);
        Assert.Equal(InferenceAccelerationMode.Gpu, coordinator.AccelerationMode);
    }

    [Fact]
    public void Navigation_UsesDistinctFluentSystemIcons()
    {
        var coordinator = new FakeCoordinator();
        var viewModel = new MainWindowViewModel(
            new HomeViewModel(coordinator, new FakeClipboard()),
            new FilesViewModel(coordinator, new FakeFilePicker()),
            new HistoryViewModel(coordinator, new FakeClipboard()),
            new ModelsViewModel(coordinator),
            new ApiViewModel(new FakeLocalApiService()),
            new SettingsViewModel(coordinator));

        Assert.Equal(
            [Icon.Translate, Icon.Document, Icon.History, Icon.BrainCircuit, Icon.PlugConnected, Icon.Settings],
            viewModel.Navigation.Select(item => item.Icon));
        Assert.Equal(viewModel.Navigation.Count, viewModel.Navigation.Select(item => item.Icon).Distinct().Count());
    }

    [Fact]
    public async Task Models_CanSelectInstalledFastModel()
    {
        var coordinator = new FakeCoordinator
        {
            ModelInfos =
            [
                new DesktopModelInfo(DesktopTranslationCoordinator.FastModelId, "Fast", "Q2_0C", 600_534_880, "极速模型已安装", true, false, true),
                new DesktopModelInfo(DesktopTranslationCoordinator.StandardModelId, "Standard", "Q4_K_M", 1_133_080_448, "标准模型已就绪", true, true, true)
            ]
        };
        var viewModel = new ModelsViewModel(coordinator);

        await viewModel.SelectFastCommand.ExecuteAsync(null);

        Assert.Equal(DesktopTranslationCoordinator.FastModelId, coordinator.SelectedModelId);
    }

    private sealed class FakeCoordinator : ITranslationCoordinator
    {
        public bool CacheEnabled { get; set; }
        public bool HistoryEnabled { get; set; } = true;
        public InferenceAccelerationMode AccelerationMode { get; set; } = InferenceAccelerationMode.Automatic;
        public string AccelerationStatus => AccelerationMode.ToString();
        public string ModelStatus => "标准模型已安装";
        public IReadOnlyList<DesktopModelInfo> ModelInfos { get; set; } = [];
        public string? SelectedModelId { get; private set; }
        public Task SelectModelAsync(string modelId, CancellationToken cancellationToken = default)
        {
            SelectedModelId = modelId;
            return Task.CompletedTask;
        }
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

    private sealed class FakeLocalApiService : ILocalApiService
    {
        public bool IsRunning => true;
        public string Endpoint => "http://127.0.0.1:17891/api/v1";
        public string? LastError => null;
        public PairingCode CreatePairingCode() => new("123456", DateTimeOffset.UtcNow.AddMinutes(5));
        public Task<IReadOnlyList<ApiClient>> ListClientsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApiClient>>([]);
        public Task<bool> RevokeClientAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
