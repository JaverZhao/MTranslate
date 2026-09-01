using MTranslate.Core;
using MTranslate.Desktop.Services;
using MTranslate.Desktop.ViewModels;
using MTranslate.DocumentFormats;

namespace MTranslate.Desktop.Tests;

public sealed class HistoryViewModelTests
{
    [Fact]
    public async Task RefreshCopyAndDelete_OperateOnPersistedEntries()
    {
        var entry = new TranslationHistoryEntry(
            Guid.NewGuid(), "Hello", "你好", "en", "zh-CN", DesktopTranslationCoordinator.FastModelId,
            DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        var coordinator = new FakeCoordinator(entry);
        var clipboard = new FakeClipboard();
        var viewModel = new HistoryViewModel(coordinator, clipboard);

        await viewModel.RefreshCommand.ExecuteAsync(null);
        var item = Assert.Single(viewModel.Entries);
        await item.CopyCommand.ExecuteAsync(null);
        await item.DeleteCommand.ExecuteAsync(null);

        Assert.Equal("你好", clipboard.Text);
        Assert.Empty(viewModel.Entries);
        Assert.True(viewModel.IsEmpty);
    }

    private sealed class FakeCoordinator(params TranslationHistoryEntry[] entries) : ITranslationCoordinator
    {
        private readonly List<TranslationHistoryEntry> history = [.. entries];
        public bool CacheEnabled { get; set; }
        public string ModelStatus => "ready";
        public Task<IReadOnlyList<TranslationHistoryEntry>> SearchHistoryAsync(string? query = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TranslationHistoryEntry>>([.. history]);
        public Task<bool> DeleteHistoryAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(history.RemoveAll(entry => entry.Id == id) > 0);
        public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
        {
            history.Clear();
            return Task.CompletedTask;
        }
        public Task<DesktopTranslationResponse> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DocumentTranslationResult> TranslateDocumentAsync(string inputPath, string outputPath, string sourceLanguage, string targetLanguage, SubtitleOutputMode subtitleOutput, Guid jobId, IProgress<DocumentTranslationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }
}
