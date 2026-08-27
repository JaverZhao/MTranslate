using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using MTranslate.Desktop.Services;

namespace MTranslate.Desktop.ViewModels;

public sealed class HomeViewModel : PageViewModel
{
    private readonly ITranslationCoordinator coordinator;
    private readonly IClipboardService clipboard;
    private CancellationTokenSource? translationCancellation;
    private string sourceText = string.Empty;
    private string translatedText = string.Empty;
    private string statusMessage;
    private string elapsedText = "尚未翻译";
    private bool isBusy;
    private LanguageOption selectedSourceLanguage;
    private LanguageOption selectedTargetLanguage;

    public HomeViewModel(ITranslationCoordinator coordinator, IClipboardService clipboard)
        : base("翻译工作台", "输入文本，所有内容只在本机处理。")
    {
        this.coordinator = coordinator;
        this.clipboard = clipboard;
        SourceLanguages =
        [
            new("auto", "自动识别"), new("en", "英语"), new("zh-CN", "简体中文"),
            new("zh-TW", "繁体中文"), new("ja", "日语"), new("ko", "韩语"),
            new("de", "德语"), new("fr", "法语"), new("es", "西班牙语")
        ];
        TargetLanguages = SourceLanguages.Where(language => language.Code != "auto").ToArray();
        Models = [new("standard", "标准 · Q4")];
        selectedSourceLanguage = SourceLanguages[0];
        selectedTargetLanguage = TargetLanguages.Single(language => language.Code == "zh-CN");
        SelectedModel = Models[0];
        statusMessage = coordinator.ModelStatus;
        TranslateCommand = new AsyncRelayCommand(TranslateAsync, CanTranslate);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        CopyCommand = new AsyncRelayCommand(CopyAsync, () => !string.IsNullOrWhiteSpace(TranslatedText));
        ClearCommand = new RelayCommand(Clear, () => !IsBusy && (SourceText.Length > 0 || TranslatedText.Length > 0));
        SwapCommand = new RelayCommand(Swap, () => !IsBusy);
    }

    public IReadOnlyList<LanguageOption> SourceLanguages { get; }
    public IReadOnlyList<LanguageOption> TargetLanguages { get; }
    public IReadOnlyList<ModelOption> Models { get; }
    public ModelOption SelectedModel { get; }
    public IAsyncRelayCommand TranslateCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand CopyCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand SwapCommand { get; }

    public string SourceText
    {
        get => sourceText;
        set
        {
            if (!SetProperty(ref sourceText, value)) return;
            OnPropertyChanged(nameof(SourceCharacterCount));
            NotifyCommandStates();
        }
    }

    public string TranslatedText
    {
        get => translatedText;
        private set
        {
            if (!SetProperty(ref translatedText, value)) return;
            OnPropertyChanged(nameof(TranslatedCharacterCount));
            NotifyCommandStates();
        }
    }

    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string ElapsedText { get => elapsedText; private set => SetProperty(ref elapsedText, value); }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            NotifyCommandStates();
        }
    }

    public LanguageOption SelectedSourceLanguage
    {
        get => selectedSourceLanguage;
        set => SetProperty(ref selectedSourceLanguage, value);
    }

    public LanguageOption SelectedTargetLanguage
    {
        get => selectedTargetLanguage;
        set => SetProperty(ref selectedTargetLanguage, value);
    }

    public int SourceCharacterCount => SourceText.Length;
    public int TranslatedCharacterCount => TranslatedText.Length;

    private bool CanTranslate() => !IsBusy && !string.IsNullOrWhiteSpace(SourceText);

    private async Task TranslateAsync()
    {
        translationCancellation?.Dispose();
        translationCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = "正在本机翻译";
        ElapsedText = "处理中";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await coordinator.TranslateAsync(
                SourceText,
                SelectedSourceLanguage.Code,
                SelectedTargetLanguage.Code,
                translationCancellation.Token);
            TranslatedText = result.Text;
            ElapsedText = $"{result.Elapsed.TotalSeconds:0.00} 秒 · {result.ChunkCount} 个分段";
            StatusMessage = result.CacheHits > 0 ? $"完成 · 命中 {result.CacheHits} 个缓存" : "翻译完成";
        }
        catch (OperationCanceledException)
        {
            ElapsedText = $"已在 {stopwatch.Elapsed.TotalSeconds:0.00} 秒取消";
            StatusMessage = "翻译已取消";
        }
        catch (Exception exception)
        {
            ElapsedText = "未完成";
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Cancel() => translationCancellation?.Cancel();

    private Task CopyAsync() => clipboard.SetTextAsync(TranslatedText);

    private void Clear()
    {
        SourceText = string.Empty;
        TranslatedText = string.Empty;
        StatusMessage = coordinator.ModelStatus;
        ElapsedText = "尚未翻译";
    }

    private void Swap()
    {
        var previousSource = SelectedSourceLanguage;
        var previousTarget = SelectedTargetLanguage;
        SelectedSourceLanguage = SourceLanguages.First(language => language.Code == previousTarget.Code);
        SelectedTargetLanguage = previousSource.Code == "auto"
            ? TargetLanguages.First(language => language.Code == "en")
            : TargetLanguages.First(language => language.Code == previousSource.Code);
        (SourceText, TranslatedText) = (TranslatedText, SourceText);
    }

    private void NotifyCommandStates()
    {
        TranslateCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
        SwapCommand.NotifyCanExecuteChanged();
    }
}
