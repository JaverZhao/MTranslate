using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using MTranslate.Api;
using MTranslate.Core;
using MTranslate.Desktop.Services;

namespace MTranslate.Desktop.ViewModels;

public sealed class HistoryViewModel : PageViewModel
{
    private readonly ITranslationCoordinator coordinator;
    private readonly IClipboardService clipboard;
    private string searchText = string.Empty;
    private string statusText = "正在读取历史记录";
    private bool isBusy;

    public HistoryViewModel(ITranslationCoordinator coordinator, IClipboardService clipboard)
        : base("翻译历史", "查看、复制和管理本机保存的翻译记录。")
    {
        this.coordinator = coordinator;
        this.clipboard = clipboard;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ClearCommand = new AsyncRelayCommand(ClearAsync, () => !IsBusy && Entries.Count > 0);
    }

    public ObservableCollection<HistoryItemViewModel> Entries { get; } = [];
    public string SearchText { get => searchText; set => SetProperty(ref searchText, value); }
    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }
    public bool IsEmpty => Entries.Count == 0;
    public bool HasEntries => Entries.Count > 0;
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            RefreshCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
        }
    }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ClearCommand { get; }

    public override void OnNavigatedTo() => RefreshCommand.Execute(null);

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var entries = await coordinator.SearchHistoryAsync(SearchText);
            Entries.Clear();
            foreach (var entry in entries)
                Entries.Add(new HistoryItemViewModel(entry, clipboard, DeleteAsync));
            StatusText = entries.Count == 0
                ? string.IsNullOrWhiteSpace(SearchText) ? "暂无翻译记录" : "没有匹配的记录"
                : $"共显示 {entries.Count} 条记录";
        }
        catch (Exception exception)
        {
            StatusText = $"读取失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyEntryState();
        }
    }

    private async Task DeleteAsync(HistoryItemViewModel item)
    {
        if (await coordinator.DeleteHistoryAsync(item.Id))
            Entries.Remove(item);
        StatusText = Entries.Count == 0 ? "暂无翻译记录" : $"共显示 {Entries.Count} 条记录";
        NotifyEntryState();
    }

    private async Task ClearAsync()
    {
        IsBusy = true;
        try
        {
            await coordinator.ClearHistoryAsync();
            Entries.Clear();
            StatusText = "历史记录已清空";
        }
        finally
        {
            IsBusy = false;
            NotifyEntryState();
        }
    }

    private void NotifyEntryState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasEntries));
        ClearCommand.NotifyCanExecuteChanged();
    }
}

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(
        TranslationHistoryEntry entry,
        IClipboardService clipboard,
        Func<HistoryItemViewModel, Task> delete)
    {
        Id = entry.Id;
        SourceText = entry.SourceText;
        TranslatedText = entry.TranslatedText;
        LanguagePair = $"{LanguageName(entry.SourceLanguage)} → {LanguageName(entry.TargetLanguage)}";
        Model = entry.ModelId == DesktopTranslationCoordinator.FastModelId ? "极速 · Q2_0C" : "标准 · Q4_K_M";
        CreatedAt = entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        Elapsed = $"{entry.Elapsed.TotalSeconds:0.00} 秒";
        CopyCommand = new AsyncRelayCommand(() => clipboard.SetTextAsync(TranslatedText));
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public Guid Id { get; }
    public string SourceText { get; }
    public string TranslatedText { get; }
    public string LanguagePair { get; }
    public string Model { get; }
    public string CreatedAt { get; }
    public string Elapsed { get; }
    public IAsyncRelayCommand CopyCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }

    private static string LanguageName(string code) =>
        TranslationLanguages.All.FirstOrDefault(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.ChineseName
        ?? code;
}

public sealed class ModelsViewModel : PageViewModel
{
    private readonly ITranslationCoordinator coordinator;
    private string standardStatus;
    private string fastStatus;
    private string fastProgress = string.Empty;
    private bool isBusy;

    public ModelsViewModel(ITranslationCoordinator coordinator)
        : base("模型", "管理本机模型、磁盘占用和当前运行状态。")
    {
        this.coordinator = coordinator;
        standardStatus = coordinator.ModelStatus;
        fastStatus = "正在读取模型状态";
        RefreshCommand = new RelayCommand(Refresh);
        DownloadFastCommand = new AsyncRelayCommand(
            () => DownloadAsync(DesktopTranslationCoordinator.FastModelId),
            () => FastCanDownload && !IsBusy);
        DownloadStandardCommand = new AsyncRelayCommand(
            () => DownloadAsync(DesktopTranslationCoordinator.StandardModelId),
            () => StandardCanDownload && !IsBusy);
        SelectFastCommand = new AsyncRelayCommand(
            () => SelectAsync(DesktopTranslationCoordinator.FastModelId),
            () => FastCanSelect && !IsBusy);
        SelectStandardCommand = new AsyncRelayCommand(
            () => SelectAsync(DesktopTranslationCoordinator.StandardModelId),
            () => StandardCanSelect && !IsBusy);
        Refresh();
    }

    public string StandardStatus { get => standardStatus; private set => SetProperty(ref standardStatus, value); }
    public string FastStatus { get => fastStatus; private set => SetProperty(ref fastStatus, value); }
    public string FastProgress { get => fastProgress; private set => SetProperty(ref fastProgress, value); }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            NotifyCommands();
        }
    }
    public bool FastCanDownload { get; private set; }
    public bool StandardCanDownload { get; private set; }
    public bool FastCanSelect { get; private set; }
    public bool StandardCanSelect { get; private set; }
    public string FastAction => FastCanDownload ? "下载模型" : "设为当前";
    public string StandardAction => StandardCanDownload ? "下载模型" : "设为当前";
    public IRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand DownloadFastCommand { get; }
    public IAsyncRelayCommand DownloadStandardCommand { get; }
    public IAsyncRelayCommand SelectFastCommand { get; }
    public IAsyncRelayCommand SelectStandardCommand { get; }

    private async Task DownloadAsync(string modelId)
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<DownloadProgress>(value =>
            {
                FastProgress = value.Percentage is { } percentage
                    ? $"已下载 {percentage:F1}%"
                    : $"已下载 {value.BytesDownloaded / 1024d / 1024d:F1} MB";
            });
            await coordinator.DownloadModelAsync(modelId, progress);
            FastProgress = string.Empty;
        }
        catch (Exception exception)
        {
            if (modelId == DesktopTranslationCoordinator.FastModelId)
                FastStatus = $"下载失败：{exception.Message}";
            else
                StandardStatus = $"下载失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private async Task SelectAsync(string modelId)
    {
        IsBusy = true;
        try
        {
            await coordinator.SelectModelAsync(modelId);
        }
        catch (Exception exception)
        {
            if (modelId == DesktopTranslationCoordinator.FastModelId)
                FastStatus = $"切换失败：{exception.Message}";
            else
                StandardStatus = $"切换失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        var models = coordinator.ModelInfos;
        var fast = models.FirstOrDefault(model => model.Id == DesktopTranslationCoordinator.FastModelId);
        var standard = models.FirstOrDefault(model => model.Id == DesktopTranslationCoordinator.StandardModelId);
        if (fast is not null)
        {
            FastStatus = fast.Status;
            FastCanDownload = fast.CanDownload;
            FastCanSelect = fast.CanSelect;
        }
        if (standard is not null)
        {
            StandardStatus = standard.Status;
            StandardCanDownload = standard.CanDownload;
            StandardCanSelect = standard.CanSelect;
        }
        OnPropertyChanged(nameof(FastCanDownload));
        OnPropertyChanged(nameof(StandardCanDownload));
        OnPropertyChanged(nameof(FastCanSelect));
        OnPropertyChanged(nameof(StandardCanSelect));
        OnPropertyChanged(nameof(FastAction));
        OnPropertyChanged(nameof(StandardAction));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        DownloadFastCommand.NotifyCanExecuteChanged();
        DownloadStandardCommand.NotifyCanExecuteChanged();
        SelectFastCommand.NotifyCanExecuteChanged();
        SelectStandardCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ApiViewModel : PageViewModel
{
    private readonly ILocalApiService api;
    private string pairingCode = "尚未生成";
    private string pairingExpiry = "配对码仅显示在本机，生成后 5 分钟有效。";

    public ApiViewModel(ILocalApiService api)
        : base("本地 API", "为浏览器扩展、OCR 和自动化工具提供受保护的本机接口。")
    {
        this.api = api;
        GeneratePairingCodeCommand = new RelayCommand(GeneratePairingCode);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RefreshAsync().GetAwaiter().GetResult();
    }

    public string Status => api.IsRunning ? "运行中" : "启动失败";
    public string StatusBadge => api.IsRunning ? "ONLINE" : "OFFLINE";
    public string Endpoint => api.Endpoint;
    public string Availability => api.IsRunning
        ? "仅监听 127.0.0.1；除健康检查和一次性配对外，所有接口都需要 Token。"
        : api.LastError ?? "本地 API 未启动。";
    public string PairingCode { get => pairingCode; private set => SetProperty(ref pairingCode, value); }
    public string PairingExpiry { get => pairingExpiry; private set => SetProperty(ref pairingExpiry, value); }
    public int ClientCount => Clients.Count(client => !client.Revoked);
    public ObservableCollection<ApiClientItemViewModel> Clients { get; } = [];
    public IRelayCommand GeneratePairingCodeCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    private void GeneratePairingCode()
    {
        var code = api.CreatePairingCode();
        PairingCode = code.Code;
        PairingExpiry = $"有效至 {code.ExpiresAt.ToLocalTime():HH:mm:ss}，成功配对后立即失效。";
    }

    private async Task RefreshAsync()
    {
        var clients = await api.ListClientsAsync();
        Clients.Clear();
        foreach (var client in clients)
            Clients.Add(new ApiClientItemViewModel(client, RevokeAsync));
        OnPropertyChanged(nameof(ClientCount));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(Availability));
    }

    private async Task RevokeAsync(ApiClientItemViewModel item)
    {
        if (await api.RevokeClientAsync(item.Id))
            await RefreshAsync();
    }
}

public sealed class ApiClientItemViewModel
{
    public ApiClientItemViewModel(ApiClient client, Func<ApiClientItemViewModel, Task> revoke)
    {
        Id = client.Id;
        Name = client.Name;
        ClientType = client.ClientType;
        LastUsed = client.LastUsedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "从未使用";
        Revoked = client.Revoked;
        RevokeCommand = new AsyncRelayCommand(() => revoke(this), () => !Revoked);
    }

    public Guid Id { get; }
    public string Name { get; }
    public string ClientType { get; }
    public string LastUsed { get; }
    public bool Revoked { get; }
    public string State => Revoked ? "已吊销" : "有效";
    public IAsyncRelayCommand RevokeCommand { get; }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly ITranslationCoordinator coordinator;
    private bool cacheEnabled;
    private bool historyEnabled = true;
    private string interfaceLanguage = "简体中文";
    private string acceleration = "自动";

    public SettingsViewModel(ITranslationCoordinator coordinator)
        : base("设置", "调整翻译、隐私、外观和运行参数。")
    {
        this.coordinator = coordinator;
        cacheEnabled = coordinator.CacheEnabled;
        historyEnabled = coordinator.HistoryEnabled;
        acceleration = coordinator.AccelerationMode switch
        {
            InferenceAccelerationMode.Cpu => "CPU",
            InferenceAccelerationMode.Gpu => "GPU",
            _ => "自动"
        };
    }

    public IReadOnlyList<string> InterfaceLanguages { get; } = ["简体中文", "English"];
    public IReadOnlyList<string> AccelerationOptions { get; } = ["自动", "CPU", "GPU"];
    public bool CacheEnabled
    {
        get => cacheEnabled;
        set
        {
            if (!SetProperty(ref cacheEnabled, value)) return;
            coordinator.CacheEnabled = value;
        }
    }
    public bool HistoryEnabled
    {
        get => historyEnabled;
        set
        {
            if (!SetProperty(ref historyEnabled, value)) return;
            coordinator.HistoryEnabled = value;
        }
    }
    public string InterfaceLanguage { get => interfaceLanguage; set => SetProperty(ref interfaceLanguage, value); }
    public string Acceleration
    {
        get => acceleration;
        set
        {
            if (!SetProperty(ref acceleration, value)) return;
            coordinator.AccelerationMode = value switch
            {
                "CPU" => InferenceAccelerationMode.Cpu,
                "GPU" => InferenceAccelerationMode.Gpu,
                _ => InferenceAccelerationMode.Automatic
            };
            OnPropertyChanged(nameof(AccelerationStatus));
        }
    }
    public string AccelerationStatus => $"当前计划：{coordinator.AccelerationStatus}";
}
