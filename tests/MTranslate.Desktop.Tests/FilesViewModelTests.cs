using MTranslate.Desktop.Services;
using MTranslate.Desktop.ViewModels;
using MTranslate.DocumentFormats;

namespace MTranslate.Desktop.Tests;

public sealed class FilesViewModelTests
{
    [Fact]
    public void AddFiles_AcceptsSupportedFilesAndRejectsDuplicates()
    {
        var directory = CreateDirectory();
        try
        {
            var textFile = Path.Combine(directory, "notes.txt");
            var unsupported = Path.Combine(directory, "image.png");
            File.WriteAllText(textFile, "Hello");
            File.WriteAllText(unsupported, "data");
            var viewModel = new FilesViewModel(new FakeCoordinator(), new FakeFilePicker());

            viewModel.AddFiles([textFile, textFile, unsupported]);

            var task = Assert.Single(viewModel.Tasks);
            Assert.Equal("notes.zh-CN.txt", Path.GetFileName(task.OutputPath));
            Assert.True(viewModel.HasTasks);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DocumentTask_ReportsCompletionAndOpensOutputFolder()
    {
        var directory = CreateDirectory();
        try
        {
            var input = Path.Combine(directory, "captions.srt");
            var output = Path.Combine(directory, "captions.zh-CN.srt");
            File.WriteAllText(input, "content");
            var coordinator = new FakeCoordinator();
            var picker = new FakeFilePicker();
            var task = new DocumentTaskViewModel(coordinator, picker, input, output);

            await task.StartCommand.ExecuteAsync(null);
            await task.OpenOutputCommand.ExecuteAsync(null);

            Assert.Equal("已完成", task.Status);
            Assert.Equal(100, task.Progress);
            Assert.Equal(output, picker.OpenedPath);
            Assert.Equal(SubtitleOutputMode.TranslationOnly, coordinator.OutputMode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LanguageSelectors_ContainEveryOfficialHyMt2Language()
    {
        var viewModel = new FilesViewModel(new FakeCoordinator(), new FakeFilePicker());

        Assert.Equal(39, viewModel.SourceLanguages.Count);
        Assert.Equal(38, viewModel.TargetLanguages.Count);
        Assert.Contains(viewModel.SourceLanguages, language => language.Code == "tr" && language.DisplayName == "土耳其语");
        Assert.Contains(viewModel.TargetLanguages, language => language.Code == "ug");
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtranslate-files-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeCoordinator : ITranslationCoordinator
    {
        public bool CacheEnabled { get; set; }
        public string ModelStatus => "ready";
        public SubtitleOutputMode? OutputMode { get; private set; }
        public Task<DesktopTranslationResponse> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DesktopTranslationResponse(text, TimeSpan.Zero, 0, 1));
        public Task<DocumentTranslationResult> TranslateDocumentAsync(string inputPath, string outputPath, string sourceLanguage, string targetLanguage, SubtitleOutputMode subtitleOutput, Guid jobId, IProgress<DocumentTranslationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            OutputMode = subtitleOutput;
            progress?.Report(new DocumentTranslationProgress(10, 10, 1, 1));
            return Task.FromResult(new DocumentTranslationResult(jobId, outputPath, 1, 10, TimeSpan.FromSeconds(1), false));
        }
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public string? OpenedPath { get; private set; }
        public Task<IReadOnlyList<string>> PickDocumentsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickOutputFolderAsync() => Task.FromResult<string?>(null);
        public Task OpenContainingFolderAsync(string path) { OpenedPath = path; return Task.CompletedTask; }
    }
}
