using MTranslate.Desktop.Services;
using MTranslate.Desktop.ViewModels;
using MTranslate.DocumentFormats;

namespace MTranslate.Desktop.Tests;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task TranslateCommand_UpdatesResultAndStatus()
    {
        var coordinator = new FakeCoordinator();
        var clipboard = new FakeClipboard();
        var viewModel = new HomeViewModel(coordinator, clipboard) { SourceText = "Hello" };

        await viewModel.TranslateCommand.ExecuteAsync(null);

        Assert.Equal("你好", viewModel.TranslatedText);
        Assert.Equal("翻译完成", viewModel.StatusMessage);
        Assert.Contains("1 个分段", viewModel.ElapsedText);
        Assert.Equal(("Hello", "auto", "zh-CN"), coordinator.LastRequest);
    }

    [Fact]
    public async Task CopyAndClearCommands_OperateOnCurrentTranslation()
    {
        var clipboard = new FakeClipboard();
        var viewModel = new HomeViewModel(new FakeCoordinator(), clipboard) { SourceText = "Hello" };
        await viewModel.TranslateCommand.ExecuteAsync(null);

        await viewModel.CopyCommand.ExecuteAsync(null);
        viewModel.ClearCommand.Execute(null);

        Assert.Equal("你好", clipboard.Text);
        Assert.Empty(viewModel.SourceText);
        Assert.Empty(viewModel.TranslatedText);
    }

    [Fact]
    public void SwapCommand_ExchangesLanguagesAndText()
    {
        var viewModel = new HomeViewModel(new FakeCoordinator(), new FakeClipboard()) { SourceText = "Hello" };

        viewModel.SwapCommand.Execute(null);

        Assert.Equal("zh-CN", viewModel.SelectedSourceLanguage.Code);
        Assert.Equal("en", viewModel.SelectedTargetLanguage.Code);
    }

    private sealed class FakeCoordinator : ITranslationCoordinator
    {
        public bool CacheEnabled { get; set; } = true;
        public string ModelStatus => "标准模型已安装";
        public (string Text, string Source, string Target)? LastRequest { get; private set; }
        public Task<DesktopTranslationResponse> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            LastRequest = (text, sourceLanguage, targetLanguage);
            return Task.FromResult(new DesktopTranslationResponse("你好", TimeSpan.FromMilliseconds(120), 0, 1));
        }
        public Task<DocumentTranslationResult> TranslateDocumentAsync(string inputPath, string outputPath, string sourceLanguage, string targetLanguage, SubtitleOutputMode subtitleOutput, Guid jobId, IProgress<DocumentTranslationProgress>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }
        public Task SetTextAsync(string text) { Text = text; return Task.CompletedTask; }
    }
}
