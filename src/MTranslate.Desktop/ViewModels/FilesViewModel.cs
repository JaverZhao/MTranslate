using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MTranslate.Desktop.Services;
using MTranslate.DocumentFormats;

namespace MTranslate.Desktop.ViewModels;

public sealed class FilesViewModel : PageViewModel
{
    private static readonly HashSet<string> SupportedExtensions = new([".txt", ".srt", ".vtt", ".md", ".markdown", ".ass"], StringComparer.OrdinalIgnoreCase);
    private readonly ITranslationCoordinator coordinator;
    private readonly IFilePickerService filePicker;
    private string statusMessage = "拖入文件，或从电脑中选择。";
    private string? outputDirectory;

    public FilesViewModel(ITranslationCoordinator coordinator, IFilePickerService filePicker)
        : base("文件翻译", "保留字幕时间码、Markdown 结构和源文件。")
    {
        this.coordinator = coordinator;
        this.filePicker = filePicker;
        AddFilesCommand = new AsyncRelayCommand(AddFilesAsync);
        ChooseOutputDirectoryCommand = new AsyncRelayCommand(ChooseOutputDirectoryAsync);
        TranslateAllCommand = new AsyncRelayCommand(TranslateAllAsync, () => Tasks.Any(task => task.CanStart));
        ClearCompletedCommand = new RelayCommand(ClearCompleted, () => Tasks.Any(task => task.Status == "已完成"));
    }

    public ObservableCollection<DocumentTaskViewModel> Tasks { get; } = [];
    public IReadOnlyList<string> SupportedFormats { get; } = ["TXT", "SRT", "VTT", "Markdown", "ASS"];
    public IAsyncRelayCommand AddFilesCommand { get; }
    public IAsyncRelayCommand ChooseOutputDirectoryCommand { get; }
    public IAsyncRelayCommand TranslateAllCommand { get; }
    public IRelayCommand ClearCompletedCommand { get; }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string OutputDirectoryText => outputDirectory ?? "与源文件相同";
    public IReadOnlyList<LanguageOption> SourceLanguages { get; } = [new("auto", "自动识别"), new("en", "英语"), new("zh-CN", "简体中文"), new("ja", "日语"), new("ko", "韩语")];
    public IReadOnlyList<LanguageOption> TargetLanguages { get; } = [new("zh-CN", "简体中文"), new("zh-TW", "繁体中文"), new("en", "英语"), new("ja", "日语")];
    public IReadOnlyList<SubtitleModeOption> SubtitleModes { get; } =
    [
        new(SubtitleOutputMode.TranslationOnly, "仅译文"),
        new(SubtitleOutputMode.OriginalThenTranslation, "原文 + 译文"),
        new(SubtitleOutputMode.TranslationThenOriginal, "译文 + 原文")
    ];
    public LanguageOption SelectedSourceLanguage { get; set; } = new("auto", "自动识别");
    public LanguageOption SelectedTargetLanguage { get; set; } = new("zh-CN", "简体中文");
    public SubtitleModeOption SelectedSubtitleMode { get; set; } = new(SubtitleOutputMode.TranslationOnly, "仅译文");
    public bool HasTasks => Tasks.Count > 0;

    public void AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path) || !SupportedExtensions.Contains(Path.GetExtension(path))
                || Tasks.Any(task => task.InputPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var extension = Path.GetExtension(path);
            var output = Path.Combine(outputDirectory ?? Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)}.{SelectedTargetLanguage.Code}{extension}");
            Tasks.Add(new DocumentTaskViewModel(
                coordinator,
                filePicker,
                path,
                output,
                SelectedSourceLanguage.Code,
                SelectedTargetLanguage.Code,
                SelectedSubtitleMode.Mode));
            added++;
        }
        StatusMessage = added == 0 ? "没有发现新的受支持文件。" : $"已加入 {added} 个文件。";
        OnPropertyChanged(nameof(HasTasks));
        NotifyCommands();
    }

    private async Task AddFilesAsync() => AddFiles(await filePicker.PickDocumentsAsync());

    private async Task ChooseOutputDirectoryAsync()
    {
        outputDirectory = await filePicker.PickOutputFolderAsync();
        OnPropertyChanged(nameof(OutputDirectoryText));
    }

    private async Task TranslateAllAsync()
    {
        foreach (var task in Tasks.Where(task => task.CanStart).ToArray())
            await task.StartCommand.ExecuteAsync(null);
        NotifyCommands();
    }

    private void ClearCompleted()
    {
        foreach (var task in Tasks.Where(task => task.Status == "已完成").ToArray())
            Tasks.Remove(task);
        OnPropertyChanged(nameof(HasTasks));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        TranslateAllCommand.NotifyCanExecuteChanged();
        ClearCompletedCommand.NotifyCanExecuteChanged();
    }
}

public sealed record SubtitleModeOption(SubtitleOutputMode Mode, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class DocumentTaskViewModel : ObservableObject
{
    private readonly ITranslationCoordinator coordinator;
    private readonly IFilePickerService filePicker;
    private CancellationTokenSource? cancellation;
    private string status = "等待中";
    private double progress;
    private string elapsed = "—";
    private bool isRunning;

    public DocumentTaskViewModel(
        ITranslationCoordinator coordinator,
        IFilePickerService filePicker,
        string inputPath,
        string outputPath,
        string sourceLanguage = "auto",
        string targetLanguage = "zh-CN",
        SubtitleOutputMode subtitleOutput = SubtitleOutputMode.TranslationOnly)
    {
        this.coordinator = coordinator;
        this.filePicker = filePicker;
        InputPath = inputPath;
        OutputPath = outputPath;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
        SubtitleOutput = subtitleOutput;
        JobId = Guid.NewGuid();
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        PauseCommand = new RelayCommand(Pause, () => IsRunning);
        OpenOutputCommand = new AsyncRelayCommand(() => filePicker.OpenContainingFolderAsync(OutputPath), () => Status == "已完成");
    }

    public string InputPath { get; }
    public string OutputPath { get; }
    public string FileName => Path.GetFileName(InputPath);
    public string Format => Path.GetExtension(InputPath).TrimStart('.').ToUpperInvariant();
    public Guid JobId { get; }
    public string SourceLanguage { get; }
    public string TargetLanguage { get; }
    public SubtitleOutputMode SubtitleOutput { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IRelayCommand PauseCommand { get; }
    public IAsyncRelayCommand OpenOutputCommand { get; }
    public bool CanStart => !IsRunning && Status != "已完成";
    public string ActionLabel => Status is "已暂停" or "失败" ? "继续" : "翻译";
    public string Status { get => status; private set { if (SetProperty(ref status, value)) { OnPropertyChanged(nameof(ActionLabel)); NotifyCommands(); } } }
    public double Progress { get => progress; private set => SetProperty(ref progress, value); }
    public string Elapsed { get => elapsed; private set => SetProperty(ref elapsed, value); }
    public bool IsRunning { get => isRunning; private set { if (SetProperty(ref isRunning, value)) { OnPropertyChanged(nameof(CanStart)); NotifyCommands(); } } }

    private async Task StartAsync()
    {
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        IsRunning = true;
        Status = "翻译中";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await coordinator.TranslateDocumentAsync(
                InputPath,
                OutputPath,
                SourceLanguage,
                TargetLanguage,
                SubtitleOutput,
                JobId,
                new System.Progress<DocumentTranslationProgress>(value => Progress = value.Percentage),
                cancellation.Token);
            stopwatch.Stop();
            Progress = 100;
            Elapsed = $"{result.Duration.TotalSeconds:0.0} 秒";
            Status = "已完成";
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            Elapsed = $"{stopwatch.Elapsed.TotalSeconds:0.0} 秒";
            Status = "已暂停";
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Elapsed = exception.Message;
            Status = "失败";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Pause() => cancellation?.Cancel();

    private void NotifyCommands()
    {
        StartCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        OpenOutputCommand.NotifyCanExecuteChanged();
    }
}
